using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.13 — runs once per day at a configurable time ("NightlyMaintenanceRunHour", default 01:00) and
// performs independent file-lifecycle tasks. A failure in one task is logged and does not block the others.
//
// v3.0: SCH files are archived at import and kept, so the old Task 3 (delete SCH on completion) is gone,
// and MPS archiving (old Task 4) now happens at the end of the shift a job completes in (MpsFileService).
// Task numbers are kept so §5.13 and the logs still line up.
public class NightlyMaintenanceService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NightlyMaintenanceService> _logger;

    public NightlyMaintenanceService(IServiceScopeFactory scopeFactory, ILogger<NightlyMaintenanceService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var runHour = await SettingsReader.GetLoopSettingAsync(_scopeFactory, "NightlyMaintenanceRunHour", 1, 0, 23, stoppingToken);

            var now = SiteClock.Now;
            var nextRun = now.Date.AddHours(runHour);
            if (nextRun <= now)
            {
                nextRun = nextRun.AddDays(1);
            }

            await Task.Delay(nextRun - now, stoppingToken);

            try
            {
                await RunAllTasksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NightlyMaintenanceService run failed.");
            }
        }
    }

    public async Task RunAllTasksAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await SafeRun(db, "Task 1 — Move MPS to DataIn", () => MpsFiles.SendDueAsync(db, ct), ct);
        await SafeRun(db, "Task 2 — Archive DONE saw folders", () => ArchiveDoneFoldersAsync(db, ct), ct);
        await SafeRun(db, "Task 5 — Retention archiving", () => ArchiveOldRecordsAsync(db, ct), ct);
    }

    private async Task SafeRun(AppDbContext db, string taskLabel, Func<Task> task, CancellationToken ct)
    {
        try
        {
            await task();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{TaskLabel} failed.", taskLabel);
            db.MaintenanceLogs.Add(new MaintenanceLog
            {
                TaskName = taskLabel,
                Outcome = "error",
                Message = ex.Message,
                RunAt = SiteClock.Now
            });
            await db.SaveChangesAsync(ct);
        }
    }

    internal static async Task ArchiveDoneFoldersAsync(AppDbContext db, CancellationToken ct)
    {
        var forCuttingPath = await SettingsReader.GetStringAsync(db, "SawFilesForCuttingPath", "", ct);
        var doneBasePath = await SettingsReader.GetStringAsync(db, "SawFilesDoneBasePath", "", ct);

        if (string.IsNullOrWhiteSpace(forCuttingPath) || string.IsNullOrWhiteSpace(doneBasePath) || !Directory.Exists(forCuttingPath))
        {
            return;
        }

        var now = SiteClock.Now;
        var archiveDir = Path.Combine(doneBasePath, now.Year.ToString(), $"{now.Year}-{now.Month:D2}");

        foreach (var folder in Directory.GetDirectories(forCuttingPath).Where(f => Path.GetFileName(f).EndsWith("DONE", StringComparison.OrdinalIgnoreCase)))
        {
            Directory.CreateDirectory(archiveDir);
            var dest = Path.Combine(archiveDir, Path.GetFileName(folder));
            var log = new MaintenanceLog { TaskName = "SawFilesCleanup", SourcePath = folder, DestPath = dest, RunAt = SiteClock.Now };

            try
            {
                if (Directory.Exists(dest))
                {
                    log.Outcome = "skipped";
                    log.Message = "Already present in archive.";
                }
                else
                {
                    Directory.Move(folder, dest);
                    log.Outcome = "success";
                }
            }
            catch (IOException ex)
            {
                log.Outcome = "error";
                log.Message = ex.Message;
            }

            db.MaintenanceLogs.Add(log);
        }

        await db.SaveChangesAsync(ct);
    }

    internal static async Task ArchiveOldRecordsAsync(AppDbContext db, CancellationToken ct)
    {
        var retentionMonths = await SettingsReader.GetIntAsync(db, "ScheduleRetentionMonths", 6, ct);
        var cutoff = SiteClock.Now.AddMonths(-retentionMonths);
        var now = SiteClock.Now;
        var archived = 0;

        archived += await db.Jobs
            .Where(j => !j.IsArchived && j.Status == "completed" && j.UpdatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.IsArchived, true).SetProperty(j => j.ArchivedAt, now), ct);

        archived += await db.ScheduleItems
            .Where(si => !si.IsArchived && si.GeneratedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(si => si.IsArchived, true).SetProperty(si => si.ArchivedAt, now), ct);

        archived += await db.BatchcutEvents
            .Where(e => !e.IsArchived && e.DetectedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.IsArchived, true).SetProperty(e => e.ArchivedAt, now), ct);

        archived += await db.MaintenanceLogs
            .Where(l => !l.IsArchived && l.RunAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.IsArchived, true).SetProperty(l => l.ArchivedAt, now), ct);

        db.MaintenanceLogs.Add(new MaintenanceLog
        {
            TaskName = "RetentionArchive",
            Outcome = "success",
            Message = $"{archived} record(s) flagged archived (older than {retentionMonths} months).",
            RunAt = now
        });
        await db.SaveChangesAsync(ct);
    }
}
