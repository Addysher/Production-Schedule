using ProductionScheduler.Domain;
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
//
// "Today" and every timestamp are site-local time (SiteClock): PaperlessJig names its files and stamps
// its events in the factory's local time. Anything skipped or unreadable is written to MaintenanceLogs
// (TaskName "PrdRead") so it shows on the Dashboard rather than only in a server log nobody reads.
public class PrdReaderService : BackgroundService
{
    private static readonly HashSet<string> KnownEvents = new(StringComparer.Ordinal)
    {
        "Version", "Program Started", "Job Opened", "Truss Selected", "Truss Removed", "Trusses Made", "Job Closed", "Program Stopped",
    };

    // PaperlessJig's timestamp format is not specified in the design doc, so accept the plausible ones.
    // Day-first on purpose: the factory's dates (SCH files) are dd/MM/yyyy.
    private static readonly string[] TimestampFormats =
    {
        "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy/MM/dd HH:mm:ss",
        "dd/MM/yyyy HH:mm:ss", "d/MM/yyyy H:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "dd-MM-yyyy HH:mm:ss",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PrdReaderService> _logger;
    private readonly Func<DateTime> _now;

    // `clock` exists so tests can prove the reader follows the site clock; production uses SiteClock.
    public PrdReaderService(IServiceScopeFactory scopeFactory, ILogger<PrdReaderService> logger, Func<DateTime>? clock = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _now = clock ?? (() => SiteClock.Now);
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

            var intervalMinutes = await SettingsReader.GetLoopSettingAsync(_scopeFactory, "PrdPollIntervalMinutes", 5, 1, 1440, stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stations = await db.Stations.Where(s => s.IsActive && s.PrdPath != null && s.PrdPath != "").ToListAsync(ct);
        var today = DateOnly.FromDateTime(_now());

        var eventsRead = 0;
        foreach (var station in stations)
        {
            // Yesterday's file first: events PaperlessJig wrote after the last poll before midnight
            // would otherwise never be read, and their jobs could never complete.
            eventsRead += await ReadStationFileAsync(db, station, today.AddDays(-1), ct);
            eventsRead += await ReadStationFileAsync(db, station, today, ct);
        }

        await RunCreepDetectionAsync(db, ct);

        // The feedback loop (v3.0): what the floor has actually made re-plans everything behind it.
        if (eventsRead > 0 && scope.ServiceProvider.GetService<IScheduleGenerationService>() is { } scheduler)
        {
            await scheduler.GenerateAsync(ct);
        }
    }

    // Returns how many production events were read.
    private async Task<int> ReadStationFileAsync(AppDbContext db, Station station, DateOnly today, CancellationToken ct)
    {
        var filePath = Path.Combine(station.PrdPath!, $"{today:yyyyMMdd}.prd");
        if (!File.Exists(filePath))
        {
            return 0;
        }

        // PaperlessJig keeps appending to this file while we read it — share for read+write.
        string text;
        await using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(stream))
        {
            text = await reader.ReadToEndAsync(ct);
        }

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var lastLineComplete = text.EndsWith('\n');
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1); // the empty piece after the final newline is not a line
        }
        else if (!lastLineComplete && lines.Count > 0
                 && (DateTime.UtcNow - File.GetLastWriteTimeUtc(filePath)).TotalSeconds < 10)
        {
            lines.RemoveAt(lines.Count - 1); // the station is probably mid-write; take it next poll
        }

        var readState = await db.PrdReadStates.FirstOrDefaultAsync(r => r.StationId == station.Id && r.FileDate == today, ct);
        if (readState is not null && readState.LastLineRead >= lines.Count)
        {
            return 0; // nothing new
        }

        // One transaction per file read: member updates, job progress and the read position commit
        // together, so an interruption means the new lines are simply read again next poll rather
        // than members ending up done while their job's progress was never recorded.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (readState is null)
        {
            readState = new PrdReadState { StationId = station.Id, FileDate = today, LastLineRead = 0, LastProcessedAt = _now() };
            db.PrdReadStates.Add(readState);
        }

        var processedEvents = 0;
        var unknownEvents = new Dictionary<string, int>();

        for (var i = readState.LastLineRead; i < lines.Count; i++)
        {
            var eventName = await ProcessLineAsync(db, lines[i], i + 1, filePath, today, ct);
            if (eventName == "Trusses Made")
            {
                processedEvents++;
            }
            else if (eventName is not null && !KnownEvents.Contains(eventName))
            {
                unknownEvents[eventName] = unknownEvents.GetValueOrDefault(eventName) + 1;
            }
        }

        foreach (var (name, count) in unknownEvents.Take(3))
        {
            MaintenanceLogWriter.Add(db, "PrdRead", "skipped",
                $"Unrecognised event '{name}' ({count} line(s)) ignored — if this should count as production, the reader needs updating", sourcePath: filePath);
        }

        if (processedEvents > 0)
        {
            MaintenanceLogWriter.Add(db, "PrdRead", "success", $"{station.Name}: {processedEvents} Trusses Made event(s) read", sourcePath: filePath);
        }

        readState.LastLineRead = lines.Count;
        readState.LastProcessedAt = _now();
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return processedEvents;
    }

    // Returns the event name on the line (null for blank lines) so the caller can tally unknown events.
    private async Task<string?> ProcessLineAsync(AppDbContext db, string line, int lineNumber, string filePath, DateOnly fileDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var fields = line.Split(',').Select(f => f.Trim()).ToArray();
        var eventName = fields[0];
        if (eventName != "Trusses Made")
        {
            return eventName; // all other event types (§5.9) are log-only / ignored
        }

        // fields[0] = "Trusses Made", then: JobRef, MemberID, timestamp, qty, f2, f3, EqA (8 fields total)
        if (fields.Length < 8)
        {
            MaintenanceLogWriter.Add(db, "PrdRead", "error",
                $"Line {lineNumber}: Trusses Made has {fields.Length} fields, expected 8 — event skipped: {line}", sourcePath: filePath);
            return eventName;
        }

        var jobRef = fields[1];
        var memberId = fields[2];

        if (!decimal.TryParse(fields[7], NumberStyles.Number, CultureInfo.InvariantCulture, out var eqaValue))
        {
            MaintenanceLogWriter.Add(db, "PrdRead", "error",
                $"Line {lineNumber}: EqA '{fields[7]}' is not a number — event skipped", jobRef, filePath);
            return eventName;
        }

        if (!TryParseTimestamp(fields[3], fileDate, out var timestamp))
        {
            timestamp = _now();
            MaintenanceLogWriter.Add(db, "PrdRead", "error",
                $"Line {lineNumber}: unreadable timestamp '{fields[3]}' — used the read time instead", jobRef, filePath);
        }

        var member = await db.JobMembers.FirstOrDefaultAsync(m => m.JobRef == jobRef && m.MemberId == memberId, ct);
        if (member is null)
        {
            _logger.LogWarning("PRD Trusses Made event for {JobRef}/{MemberId} has no matching JobMember.", jobRef, memberId);
            MaintenanceLogWriter.Add(db, "PrdRead", "skipped",
                $"Line {lineNumber}: no member {jobRef}/{memberId} in the system (job not imported yet, or the IDs differ from the SCH file)", jobRef, filePath);
            return eventName;
        }

        if (member.Done)
        {
            return eventName; // already recorded — idempotent re-read
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
        return eventName;
    }

    // Full timestamps in the usual layouts, or a bare time — in which case the file's own date (its name) applies.
    internal static bool TryParseTimestamp(string text, DateOnly fileDate, out DateTime result)
    {
        if (DateTime.TryParseExact(text, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
        {
            return true;
        }

        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            result = fileDate.ToDateTime(time);
            return true;
        }

        result = default;
        return false;
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

        // A member being made means production has started — even if the DONE folder for the cut was
        // never seen. Left 'ready'/'approved', the scheduler would keep re-planning the job and its
        // recorded progress would be thrown away with the old plan.
        if (job.Status is "ready" or "approved" or "cut")
        {
            if (job.Status != "cut")
            {
                MaintenanceLogWriter.Add(db, "PrdRead", "skipped",
                    $"{jobRef} started production while still '{job.Status}' (no cut was recorded) — moved to in-production.", jobRef);
            }

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
        var now = _now();

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
