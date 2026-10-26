using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// The MPS file's journey (v3.0): it waits in the product's pool folder → is moved overnight into the
// assigned station's DataIn before production starts → follows the job if it is rescheduled (or goes
// back to the pool if the job moves out of range) → is archived to the station's archive folder at
// the end of the shift the job completed in, or when the job is deleted.
// Jobs.MpsStationId records where it currently is: NULL = still in the pool.
public static class MpsFiles
{
    private static readonly string[] NotStarted = ["ready", "approved", "cut"];

    // Real MPS files haven't been seen yet: v1.0 says "{JobRef}__.MPS", and SCH files arrive with
    // suffixes too ("60656MTG__-RT.SCH"), so accept any .mps file named after the job.
    public static string? Find(string? folder, string jobRef)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder).FirstOrDefault(f =>
            Path.GetExtension(f).Equals(".mps", StringComparison.OrdinalIgnoreCase)
            && (Path.GetFileNameWithoutExtension(f).Equals(jobRef, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(f).StartsWith(jobRef + "_", StringComparison.OrdinalIgnoreCase)));
    }

    private static string MoveInto(string file, string folder)
    {
        Directory.CreateDirectory(folder);
        var dest = Path.Combine(folder, Path.GetFileName(file));
        File.Move(file, dest, overwrite: true);
        return dest;
    }

    private static async Task<DateOnly> HorizonAsync(AppDbContext db, CancellationToken ct) =>
        DateOnly.FromDateTime(SiteClock.Now).AddDays(await SettingsReader.GetIntAsync(db, "MpsPreMoveDaysBeforeProduction", 1, ct));

    // Overnight (Nightly Task 1): send every MPS whose job starts within MpsPreMoveDaysBeforeProduction days.
    public static Task SendDueAsync(AppDbContext db, CancellationToken ct) => SendAsync(db, overnight: true, ct);

    private static async Task SendAsync(AppDbContext db, bool overnight, CancellationToken ct)
    {
        // During the day only jobs starting today are caught up (scheduled at short notice, after the
        // overnight run); problems are only reported by the overnight run, so they aren't logged every minute.
        var horizon = overnight ? await HorizonAsync(db, ct) : DateOnly.FromDateTime(SiteClock.Now);

        var jobs = (await db.Jobs
                .Include(j => j.AssignedStation)
                .Where(j => !j.IsArchived && j.MpsStationId == null && j.MpsArchivedAt == null
                    && j.AssignedStationId != null && j.EstimatedStartDate != null
                    && (NotStarted.Contains(j.Status) || j.Status == "in-production"))
                .ToListAsync(ct))
            .Where(j => DateOnly.FromDateTime(j.EstimatedStartDate!.Value) <= horizon)
            .ToList();

        foreach (var job in jobs)
        {
            var station = job.AssignedStation!;
            if (string.IsNullOrWhiteSpace(station.UncPath))
            {
                if (overnight)
                {
                    MaintenanceLogWriter.Add(db, "MpsToDataIn", "error", $"Station '{station.Name}' has no DataIn folder set, so the MPS can't be sent.", job.JobRef);
                }
                continue;
            }

            var file = Find(job.MpsSourcePath, job.JobRef);
            if (file is null)
            {
                if (overnight)
                {
                    MaintenanceLogWriter.Add(db, "MpsToDataIn", "error",
                        $"No MPS file for {job.JobRef} in the pool folder — it starts {job.EstimatedStartDate:ddd d MMM} on {station.Name}.", job.JobRef, job.MpsSourcePath);
                }
                continue;
            }

            Move(db, job, file, station.UncPath, "MpsToDataIn", $"Sent to {station.Name} for its {job.EstimatedStartDate:ddd d MMM} start.", () => job.MpsStationId = station.Id);
        }

        await db.SaveChangesAsync(ct);
    }

    // Runs every minute: catch-up sends, follow reschedules, and end-of-shift archiving.
    public static async Task ReconcileAsync(AppDbContext db, CancellationToken ct)
    {
        await SendAsync(db, overnight: false, ct);

        var horizon = await HorizonAsync(db, ct);
        var sent = await db.Jobs
            .Include(j => j.MpsStation)
            .Include(j => j.AssignedStation)
            .Where(j => j.MpsStationId != null && j.MpsArchivedAt == null && NotStarted.Contains(j.Status))
            .ToListAsync(ct);

        foreach (var job in sent)
        {
            var file = Find(job.MpsStation!.UncPath, job.JobRef);
            if (file is null)
            {
                continue; // someone already moved it by hand — nothing to follow
            }

            var inRange = job.AssignedStation is not null && job.EstimatedStartDate is DateTime start && DateOnly.FromDateTime(start) <= horizon;
            if (!inRange && !string.IsNullOrWhiteSpace(job.MpsSourcePath))
            {
                Move(db, job, file, job.MpsSourcePath, "MpsReturn",
                    $"Rescheduled out of range — returned from {job.MpsStation.Name} to the pool.", () => job.MpsStationId = null);
            }
            else if (inRange && job.AssignedStationId != job.MpsStationId && !string.IsNullOrWhiteSpace(job.AssignedStation!.UncPath))
            {
                var from = job.MpsStation.Name;
                var to = job.AssignedStation;
                Move(db, job, file, to.UncPath!, "MpsToDataIn", $"Rescheduled — moved from {from} to {to.Name}.", () => job.MpsStationId = to.Id);
            }
        }

        if (SiteClock.Now.Hour >= await SettingsReader.GetIntAsync(db, "ShiftEndHour", 18, ct))
        {
            await ArchiveCompletedAsync(db, ct);
        }

        await db.SaveChangesAsync(ct);
    }

    // At the end of the shift a job completed in, its MPS leaves the station's DataIn for the station archive.
    private static async Task ArchiveCompletedAsync(AppDbContext db, CancellationToken ct)
    {
        var done = await db.Jobs
            .Include(j => j.MpsStation)
            .Where(j => j.Status == "completed" && j.MpsStationId != null && j.MpsArchivedAt == null)
            .ToListAsync(ct);

        foreach (var job in done.Where(j => !string.IsNullOrWhiteSpace(j.MpsStation!.ArchivePath)))
        {
            var file = Find(job.MpsStation!.UncPath, job.JobRef);
            if (file is null)
            {
                MaintenanceLogWriter.Add(db, "MpsArchive", "skipped", "MPS no longer in DataIn (moved by hand?).", job.JobRef, job.MpsStation.UncPath);
                job.MpsArchivedAt = SiteClock.Now;
                continue;
            }

            Move(db, job, file, job.MpsStation.ArchivePath!, "MpsArchive", $"Job completed — archived from {job.MpsStation.Name}.", () => job.MpsArchivedAt = SiteClock.Now);
        }
    }

    // A deleted job's MPS is archived from wherever it is: the station (to the station's archive), or
    // still in the pool (to the product's archive, alongside its SCH file). The caller saves.
    public static async Task ArchiveForDeletedJobAsync(AppDbContext db, Job job, CancellationToken ct = default)
    {
        if (job.MpsArchivedAt is not null)
        {
            return;
        }

        string? from, to;
        if (job.MpsStationId is int stationId && await db.Stations.FindAsync([stationId], ct) is { } station)
        {
            (from, to) = (station.UncPath, station.ArchivePath);
        }
        else
        {
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Name == job.ComponentType, ct);
            (from, to) = (job.MpsSourcePath, product?.ArchivePath);
        }

        var file = Find(from, job.JobRef);
        if (file is null)
        {
            return; // nothing to archive
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            MaintenanceLogWriter.Add(db, "MpsArchive", "error", $"Job {job.JobRef} deleted, but there's no archive folder set to move its MPS to.", job.JobRef, file);
            return;
        }

        Move(db, job, file, to, "MpsArchive", "Job deleted — MPS archived.", () => job.MpsArchivedAt = SiteClock.Now);
    }

    private static void Move(AppDbContext db, Job job, string file, string folder, string task, string message, Action onSuccess)
    {
        try
        {
            var dest = MoveInto(file, folder);
            onSuccess();
            db.MaintenanceLogs.Add(new MaintenanceLog
            {
                TaskName = task, JobRef = job.JobRef, SourcePath = file, DestPath = dest, Outcome = "success", Message = message, RunAt = SiteClock.Now,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MaintenanceLogWriter.Add(db, task, "error", $"Couldn't move the MPS to '{folder}': {ex.Message}", job.JobRef, file);
        }
    }
}
