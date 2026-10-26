using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.12 — watches BatchcutWatchPaths entries for job folders renamed to "{JobRef}__DONE" by
// batchcut operators, and applies the approved -> cut status transition.
//
// The schema has no column distinguishing a "for cutting" entry from a "done archive base" entry
// (§6.3 just says both are rows in the same table), and the done-archive path is described as
// needing the current \YYYY\YYYY-MM\ appended at runtime. Rather than guess a naming convention,
// this service checks each active entry's UncPath both literally and with today's \YYYY\YYYY-MM\
// suffix — which covers a "for cutting" row (folders live directly under it) and a "done base"
// row (folders live in this month's subfolder) with one uniform pass.
//
// Idempotency: BatchcutEvents itself is the dedupe store — a (BatchcutPathId, FolderName) pair
// already logged (applied/duplicate/error) is never reprocessed, since a DONE folder is not moved
// or deleted by this service (only Nightly Task 2 archives it).
public class BatchcutWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BatchcutWatcherService> _logger;

    public BatchcutWatcherService(IServiceScopeFactory scopeFactory, ILogger<BatchcutWatcherService> logger)
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
                _logger.LogError(ex, "BatchcutWatcherService poll failed.");
            }

            var intervalSeconds = await SettingsReader.GetLoopSettingAsync(_scopeFactory, "BatchcutPollIntervalSeconds", 30, 5, 86400, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var watchPaths = await db.BatchcutWatchPaths.Where(p => p.IsActive).ToListAsync(ct);
        var now = SiteClock.Now;

        foreach (var watchPath in watchPaths)
        {
            var candidateDirs = new[]
            {
                watchPath.UncPath,
                Path.Combine(watchPath.UncPath, now.Year.ToString(), $"{now.Year}-{now.Month:D2}")
            }.Distinct();

            foreach (var dir in candidateDirs)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (var folder in Directory.GetDirectories(dir))
                {
                    var folderName = Path.GetFileName(folder);
                    if (!folderName.EndsWith("DONE", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    await ProcessFolderAsync(db, watchPath, folderName, ct);
                }
            }
        }
    }

    private async Task ProcessFolderAsync(AppDbContext db, BatchcutWatchPath watchPath, string folderName, CancellationToken ct)
    {
        var alreadyProcessed = await db.BatchcutEvents
            .AnyAsync(e => e.BatchcutPathId == watchPath.Id && e.FolderName == folderName, ct);
        if (alreadyProcessed)
        {
            return;
        }

        var jobRef = ResolveJobRef(folderName);
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef, ct);
        var now = SiteClock.Now;

        if (job is null)
        {
            db.BatchcutEvents.Add(new BatchcutEvent
            {
                BatchcutPathId = watchPath.Id,
                FolderName = folderName,
                JobRef = jobRef,
                DetectedAt = now,
                Status = "error",
                ErrorMessage = "No matching job in system"
            });
            MaintenanceLogWriter.Add(db, "Batchcut", "error", $"DONE folder '{folderName}' has no matching job ('{jobRef}').", jobRef, Path.Combine(watchPath.UncPath, folderName));
            _logger.LogWarning("Batchcut DONE folder {Folder} has no matching job (resolved ref '{JobRef}').", folderName, jobRef);
        }
        else if (job.Status is "cut" or "in-production" or "completed")
        {
            db.BatchcutEvents.Add(new BatchcutEvent
            {
                BatchcutPathId = watchPath.Id,
                FolderName = folderName,
                JobRef = jobRef,
                DetectedAt = now,
                Status = "duplicate"
            });
        }
        else if (job.Status == "approved")
        {
            job.Status = "cut";
            job.UpdatedAt = now;
            db.BatchcutEvents.Add(new BatchcutEvent
            {
                BatchcutPathId = watchPath.Id,
                FolderName = folderName,
                JobRef = jobRef,
                DetectedAt = now,
                Status = "applied"
            });
            _logger.LogInformation("Job {JobRef} marked 'cut' via batchcut DONE detection.", jobRef);
        }
        else
        {
            // Job exists but isn't 'approved' yet (e.g. still 'ready') — record as an error rather
            // than silently applying a transition the state machine doesn't expect.
            db.BatchcutEvents.Add(new BatchcutEvent
            {
                BatchcutPathId = watchPath.Id,
                FolderName = folderName,
                JobRef = jobRef,
                DetectedAt = now,
                Status = "error",
                ErrorMessage = $"Job status is '{job.Status}', expected 'approved'."
            });
            MaintenanceLogWriter.Add(db, "Batchcut", "error",
                $"DONE folder '{folderName}' found, but {jobRef} is '{job.Status}' — it was never approved for cutting.", jobRef, Path.Combine(watchPath.UncPath, folderName));
        }

        await db.SaveChangesAsync(ct);
    }

    // "59778MWF__DONE" -> strip "DONE" -> "59778MWF__" -> strip trailing '_' -> "59778MWF"
    internal static string ResolveJobRef(string folderName)
    {
        var withoutDone = folderName[..^"DONE".Length];
        return withoutDone.TrimEnd('_');
    }
}
