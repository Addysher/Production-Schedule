using ProductionScheduler.Infrastructure;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.9 — polls each registered station's UNC path for today's PRD file, processes new "Trusses
// Made" lines since PrdReadState.LastLineRead, and drives the cut -> in-production -> completed
// transitions (§5.12 relationship table). §5.14 creep detection runs once per poll cycle after
// event processing.
public class PrdReaderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrdReaderService> _logger;

    public PrdReaderService(IServiceScopeFactory scopeFactory, ILogger<PrdReaderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PrdReaderService poll failed.");
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var intervalMinutes = await SettingsReader.GetIntAsync(db, "PrdPollIntervalMinutes", 5, stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stations = await db.Stations.Where(s => s.IsActive && s.UncPath != null).ToListAsync(ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var station in stations)
        {
            await ReadStationFileAsync(db, station, today, ct);
        }

        await RunCreepDetectionAsync(db, ct);
    }

    private async Task ReadStationFileAsync(AppDbContext db, Station station, DateOnly today, CancellationToken ct)
    {
        var filePath = Path.Combine(station.UncPath!, $"{today:yyyyMMdd}.prd");
        if (!File.Exists(filePath))
        {
            return;
        }

        var lines = await File.ReadAllLinesAsync(filePath, ct);

        var readState = await db.PrdReadStates.FirstOrDefaultAsync(r => r.StationId == station.Id && r.FileDate == today, ct);
        if (readState is null)
        {
            readState = new PrdReadState { StationId = station.Id, FileDate = today, LastLineRead = 0, LastProcessedAt = DateTime.UtcNow };
            db.PrdReadStates.Add(readState);
        }

        if (readState.LastLineRead >= lines.Length)
        {
            return; // nothing new
        }

        for (var i = readState.LastLineRead; i < lines.Length; i++)
        {
            await ProcessLineAsync(db, lines[i], ct);
        }

        readState.LastLineRead = lines.Length;
        readState.LastProcessedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessLineAsync(AppDbContext db, string line, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var fields = line.Split(',').Select(f => f.Trim()).ToArray();
        if (fields.Length == 0 || fields[0] != "Trusses Made")
        {
            return; // all other event types (§5.9) are log-only / ignored
        }

        // fields[0] = "Trusses Made", then: JobRef, MemberID, timestamp, qty, f2, f3, EqA (8 fields total)
        if (fields.Length < 8)
        {
            return;
        }

        var jobRef = fields[1];
        var memberId = fields[2];
        if (!DateTime.TryParse(fields[3], CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            timestamp = DateTime.UtcNow;
        }
        if (!decimal.TryParse(fields[7], NumberStyles.Number, CultureInfo.InvariantCulture, out var eqaValue))
        {
            return;
        }

        var member = await db.JobMembers.FirstOrDefaultAsync(m => m.JobRef == jobRef && m.MemberId == memberId, ct);
        if (member is null)
        {
            _logger.LogWarning("PRD Trusses Made event for {JobRef}/{MemberId} has no matching JobMember (job not imported yet?).", jobRef, memberId);
            return;
        }

        if (member.Done)
        {
            return; // already recorded — idempotent re-read
        }

        member.Done = true;
        member.CompletedAt = timestamp;
        member.CompletedEqA = eqaValue;

        // Flush before the completion check below — it counts JobMembers.Done via a SQL aggregate,
        // which (unlike entity materialization) never sees this context's pending in-memory change.
        // Without this, two members completing in the same poll cycle would miss the transition to
        // 'completed' until some later poll happened to run.
        await db.SaveChangesAsync(ct);

        await ApplyProductionProgressAsync(db, jobRef, eqaValue, timestamp, ct);
    }

    private static async Task ApplyProductionProgressAsync(AppDbContext db, string jobRef, decimal eqaValue, DateTime timestamp, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef, ct);
        if (job is null)
        {
            return;
        }

        job.CompletedEqA += eqaValue;

        var scheduleItem = await db.ScheduleItems
            .Where(si => si.JobRef == jobRef && !si.IsArchived)
            .OrderByDescending(si => si.GeneratedAt)
            .FirstOrDefaultAsync(ct);

        if (job.Status == "cut")
        {
            job.Status = "in-production";
            if (scheduleItem is not null)
            {
                scheduleItem.ActualStart = timestamp;
            }
        }

        if (scheduleItem is not null)
        {
            scheduleItem.EqaCompleted += eqaValue;
            scheduleItem.MembersCompleted += 1;
        }

        job.UpdatedAt = timestamp;

        var totalMembers = await db.JobMembers.CountAsync(m => m.JobRef == jobRef, ct);
        var doneMembers = await db.JobMembers.CountAsync(m => m.JobRef == jobRef && m.Done, ct);

        if (totalMembers > 0 && doneMembers == totalMembers)
        {
            job.Status = "completed";
            if (scheduleItem is not null)
            {
                scheduleItem.ActualEnd = timestamp;
            }
        }
    }

    // §5.14 — flags creep when the actual EqA completion rate falls behind the planned rate by
    // more than CreepThresholdPercent. The schedule does not adjust until an admin approves it.
    private async Task RunCreepDetectionAsync(AppDbContext db, CancellationToken ct)
    {
        var thresholdPercent = await SettingsReader.GetIntAsync(db, "CreepThresholdPercent", 20, ct);
        var now = DateTime.UtcNow;

        var inFlightItems = await db.ScheduleItems
            .Where(si => !si.IsArchived && !si.CreepDetected && si.ActualStart != null)
            .Include(si => si.Job)
            .Where(si => si.Job.Status == "in-production")
            .ToListAsync(ct);

        foreach (var item in inFlightItems)
        {
            var plannedDurationHours = (item.PlannedEnd - item.PlannedStart).TotalHours;
            if (plannedDurationHours <= 0)
            {
                continue;
            }

            var plannedRate = item.PlannedEqA / (decimal)plannedDurationHours; // EqA per hour
            var elapsedHours = (decimal)(now - item.ActualStart!.Value).TotalHours;
            var expectedEqA = plannedRate * elapsedHours;

            if (expectedEqA <= 0)
            {
                continue;
            }

            var creepRatio = (expectedEqA - item.EqaCompleted) / expectedEqA;
            if (creepRatio > thresholdPercent / 100m)
            {
                item.CreepDetected = true;
                item.CreepDetectedAt = now;
                _logger.LogInformation("Creep detected on job {JobRef} — {Ratio:P1} behind planned rate.", item.JobRef, creepRatio);
            }
        }

        if (inFlightItems.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
