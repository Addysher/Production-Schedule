using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.13 — runs once per day at a configurable time (default 01:00) and performs five independent
// file-lifecycle tasks. A failure in one task is logged and does not block the others.
//
// The doc never names a Settings key for the run time itself (only "configurable, default 01:00")
// — this uses "NightlyMaintenanceRunHour" (default 1), following the same naming convention as
// ShiftStartHour/ShiftEndHour. Task 5 (retention) likewise has no TaskName value among the four
// MaintenanceLogs.TaskName examples the schema comment lists — that column is a plain NVARCHAR,
// not a constrained enum, so this logs it under "RetentionArchive".
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
            int runHour;
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                runHour = await SettingsReader.GetIntAsync(db, "NightlyMaintenanceRunHour", 1, stoppingToken);
            }

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

        await SafeRun(db, "Task 1 — Move MPS to DataIn", () => MoveMpsToDataInAsync(db, ct), ct);
        await SafeRun(db, "Task 2 — Archive DONE saw folders", () => ArchiveDoneFoldersAsync(db, ct), ct);
        await SafeRun(db, "Task 3 — Delete SCH on completion", () => DeleteSchOnCompletionAsync(db, ct), ct);
        await SafeRun(db, "Task 4 — Archive MPS on completion", () => ArchiveMpsOnCompletionAsync(db, ct), ct);
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

    internal static async Task MoveMpsToDataInAsync(AppDbContext db, CancellationToken ct)
    {
        var preMoveDays = await SettingsReader.GetIntAsync(db, "MpsPreMoveDaysBeforeProduction", 1, ct);
        var targetDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(preMoveDays);

        var jobs = await db.Jobs
            .Include(j => j.AssignedStation)
            .Where(j => j.MpsSourcePath != null && j.AssignedStationId != null && j.EstimatedStartDate != null
                && j.Status != "completed" && j.MpsArchivedAt == null) // never re-stage a finished job's MPS
            .ToListAsync(ct);

        foreach (var job in jobs.Where(j => DateOnly.FromDateTime(j.EstimatedStartDate!.Value) == targetDate))
        {
            var sourceFile = Path.Combine(job.MpsSourcePath!, $"{job.JobRef}__.MPS");
            var destFile = Path.Combine(job.AssignedStation!.UncPath ?? "", $"{job.JobRef}__.MPS");

            var log = new MaintenanceLog { TaskName = "MpsToDatIn", JobRef = job.JobRef, SourcePath = sourceFile, DestPath = destFile, RunAt = SiteClock.Now };

            if (File.Exists(destFile))
            {
                log.Outcome = "skipped";
                log.Message = "Already present in station DataIn.";
            }
            else if (!File.Exists(sourceFile))
            {
                log.Outcome = "error";
                log.Message = "Source MPS file not found.";
            }
            else
            {
                File.Copy(sourceFile, destFile);
                log.Outcome = "success";
            }

            db.MaintenanceLogs.Add(log);
        }

        await db.SaveChangesAsync(ct);
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

    internal static async Task DeleteSchOnCompletionAsync(AppDbContext db, CancellationToken ct)
    {
        var jobs = await db.Jobs
            .Where(j => j.Status == "completed" && j.SchSourcePath != null && j.SchDeletedAt == null)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            var log = new MaintenanceLog { TaskName = "SchDelete", JobRef = job.JobRef, SourcePath = job.SchSourcePath, RunAt = SiteClock.Now };

            if (!File.Exists(job.SchSourcePath))
            {
                log.Outcome = "skipped";
                log.Message = "SCH file already gone.";
            }
            else
            {
                File.Delete(job.SchSourcePath!);
                log.Outcome = "success";
            }

            job.SchDeletedAt = SiteClock.Now;
            db.MaintenanceLogs.Add(log);
        }

        await db.SaveChangesAsync(ct);
    }

    internal static async Task ArchiveMpsOnCompletionAsync(AppDbContext db, CancellationToken ct)
    {
        var jobs = await db.Jobs
            .Include(j => j.AssignedStation)
            .Where(j => j.Status == "completed" && j.AssignedStationId != null && j.MpsArchivedAt == null)
            .ToListAsync(ct);

        foreach (var job in jobs.Where(j => !string.IsNullOrWhiteSpace(j.AssignedStation!.ArchivePath)))
        {
            var sourceFile = Path.Combine(job.AssignedStation!.UncPath ?? "", $"{job.JobRef}__.MPS");
            var destFile = Path.Combine(job.AssignedStation.ArchivePath!, $"{job.JobRef}__.MPS");

            var log = new MaintenanceLog { TaskName = "MpsArchive", JobRef = job.JobRef, SourcePath = sourceFile, DestPath = destFile, RunAt = SiteClock.Now };

            if (!File.Exists(sourceFile))
            {
                log.Outcome = "skipped";
                log.Message = "MPS file not found in DataIn (operator may have already moved it).";
            }
            else
            {
                File.Move(sourceFile, destFile);
                log.Outcome = "success";
            }

            job.MpsArchivedAt = SiteClock.Now;
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
