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

    // Overnight (Nightly Task 1): send every MPS whose job starts within MpsPreMoveDaysBeforeProduction days,
    // and report finished jobs whose MPS can't be archived because their station has no archive folder.
    public static async Task SendDueAsync(AppDbContext db, CancellationToken ct)
    {
        await SendAsync(db, overnight: true, ct);

        var stuck = await db.Jobs
            .Where(j => j.Status == "completed" && j.MpsArchivedAt == null && j.MpsStation != null
                && (j.MpsStation.ArchivePath == null || j.MpsStation.ArchivePath == ""))
            .Select(j => new { j.JobRef, Station = j.MpsStation!.Name })
            .ToListAsync(ct);
        foreach (var s in stuck)
        {
            MaintenanceLogWriter.Add(db, "MpsArchive", "error", $"{s.JobRef} is finished but station '{s.Station}' has no archive folder set, so its MPS stays in DataIn.", s.JobRef);
        }

        await db.SaveChangesAsync(ct);
    }

    // Moves a file that's about to be replaced into a "_superseded" subfolder beside it.
    private static void SetAside(string file)
    {
        var folder = Path.Combine(Path.GetDirectoryName(file)!, "_superseded");
        Directory.CreateDirectory(folder);
        File.Move(file, Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(file)}_{SiteClock.Now:yyyyMMddHHmmss}{Path.GetExtension(file)}"), overwrite: true);
    }

    private static async Task SendAsync(AppDbContext db, bool overnight, CancellationToken ct)
    {
        // During the day only jobs starting today are caught up (scheduled at short notice, after the
        // overnight run); problems are only reported by the overnight run, so they aren't logged every minute.
        var horizon = overnight ? await HorizonAsync(db, ct) : DateOnly.FromDateTime(SiteClock.Now);

        var jobs = (await db.Jobs
                .Include(j => j.AssignedStation)
                .Where(j => !j.IsArchived && j.MpsStationId == null && j.MpsArchivedAt == null
                    && j.AssignedStationId != null && j.EstimatedStartDate != null
                    // A TBA job (no booked delivery date) isn't sent to a line until it's booked (v3.0).
                    && ((NotStarted.Contains(j.Status) && j.BookedDeliveryDate != null) || j.Status == "in-production"))
                .ToListAsync(ct))
            .Where(j => DateOnly.FromDateTime(j.EstimatedStartDate!.Value) <= horizon)
            .ToList();

        foreach (var job in jobs)
        {
            var station = job.AssignedStation!;
            if (string.IsNullOrWhiteSpace(station.MpsPath))
            {
                if (overnight)
                {
                    MaintenanceLogWriter.Add(db, "MpsToDataIn", "error", $"Station '{station.Name}' has no MPS folder (DataIn) set, so the MPS can't be sent.", job.JobRef);
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

            Move(db, job, file, station.MpsPath, "MpsToDataIn", $"Sent to {station.Name} for its {job.EstimatedStartDate:ddd d MMM} start.", () => job.MpsStationId = station.Id);
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
            var file = Find(job.MpsStation!.MpsPath, job.JobRef);

            // A new MPS in the pool for a job whose MPS was already sent is a revision: it replaces the
            // station's copy (kept aside, not overwritten), so the station never runs the old design.
            if (Find(job.MpsSourcePath, job.JobRef) is { } revision && !string.IsNullOrWhiteSpace(job.MpsStation.MpsPath))
            {
                try
                {
                    if (file is not null)
                    {
                        SetAside(file);
                    }

                    Move(db, job, revision, job.MpsStation.MpsPath, "MpsToDataIn", $"Revised MPS sent to {job.MpsStation.Name}.", () => { });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    MaintenanceLogWriter.Add(db, "MpsToDataIn", "error", $"Couldn't replace the MPS at {job.MpsStation.Name} with the revised one: {ex.Message}", job.JobRef, revision);
                }

                file = Find(job.MpsStation.MpsPath, job.JobRef);
            }

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
            else if (inRange && job.AssignedStationId != job.MpsStationId && !string.IsNullOrWhiteSpace(job.AssignedStation!.MpsPath))
            {
                var from = job.MpsStation.Name;
                var to = job.AssignedStation;
                Move(db, job, file, to.MpsPath!, "MpsToDataIn", $"Rescheduled — moved from {from} to {to.Name}.", () => job.MpsStationId = to.Id);
            }
        }

        // "The shift is over" = past the end of today's working day: the latest active shift end, or the
        // ShiftEndHour setting when no shift is active.
        var shiftEnd = await db.Shifts.AnyAsync(sh => sh.IsActive, ct)
            ? (await WorkCalendarSettings.LoadAsync(db, ct)).DayEnd
            : TimeSpan.FromHours(await SettingsReader.GetIntAsync(db, "ShiftEndHour", 18, ct));
        await ArchiveCompletedAsync(db, SiteClock.Now.TimeOfDay >= shiftEnd, ct);

        await db.SaveChangesAsync(ct);
    }

    // At the end of the shift a job completed in, its MPS leaves the station's DataIn for the station
    // archive. A job that finished on an earlier day is archived whatever the time (a shift that runs to
    // midnight never reaches "after the end of the shift" on the same day).
    private static async Task ArchiveCompletedAsync(AppDbContext db, bool shiftOver, CancellationToken ct)
    {
        var today = SiteClock.Now.Date;
        var done = (await db.Jobs
                .Include(j => j.MpsStation)
                .Where(j => j.Status == "completed" && j.MpsStationId != null && j.MpsArchivedAt == null)
                .ToListAsync(ct))
            .Where(j => shiftOver || j.UpdatedAt.Date < today);

        foreach (var job in done.Where(j => !string.IsNullOrWhiteSpace(j.MpsStation!.ArchivePath)))
        {
            var file = Find(job.MpsStation!.MpsPath, job.JobRef);
            if (file is null)
            {
                MaintenanceLogWriter.Add(db, "MpsArchive", "skipped", "MPS no longer in DataIn (moved by hand?).", job.JobRef, job.MpsStation.MpsPath);
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
            (from, to) = (station.MpsPath, station.ArchivePath);
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
