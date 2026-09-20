using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling;

public interface IJobStatusService
{
    Task<int> RefreshAllAsync(CancellationToken ct = default);
}

// Recalculates and persists Jobs.Status for all active jobs, called on app load via
// GET /api/jobs/status-refresh (§8.2).
//
// §8.2's "status is written back on every refresh" describes the At Risk model, which that same
// section marks superseded by §5.14's 7-state model. Under the current model, "Late" (§5.0) is a
// display-layer override — computed at read time from due date vs now — rather than a persisted
// workflow state: the BatchcutWatcherService and PrdReaderService (§4.4, §5.12) own the
// 'ready' -> 'approved' -> 'cut' -> 'in-production' transitions, and overwriting Status here with
// 'late' would erase the state their processing depends on (e.g. approve-for-cutting requires
// Status = 'ready'). Refresh's persisted job is therefore just the one transition it alone owns:
// flipping to 'completed' once every JobMember is done.
public class JobStatusService : IJobStatusService
{
    private readonly AppDbContext _db;

    public JobStatusService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> RefreshAllAsync(CancellationToken ct = default)
    {
        var candidates = await _db.Jobs
            .Where(j => !j.IsArchived && j.Status != "completed" && j.Status != "scheduled")
            .Select(j => j.Id)
            .ToListAsync(ct);

        var updated = 0;

        foreach (var jobId in candidates)
        {
            var job = await _db.Jobs.FirstAsync(j => j.Id == jobId, ct);
            var hasMembers = await _db.JobMembers.AnyAsync(m => m.JobRef == job.JobRef, ct);
            var allDone = hasMembers && !await _db.JobMembers.AnyAsync(m => m.JobRef == job.JobRef && !m.Done, ct);

            if (allDone && job.Status != "completed")
            {
                job.Status = "completed";
                job.UpdatedAt = SiteClock.Now;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _db.SaveChangesAsync(ct);
        }

        return updated;
    }

    public static bool IsLate(DateOnly? dueDate, string status, DateTime now) =>
        status != "completed" && dueDate is not null && dueDate.Value.ToDateTime(TimeOnly.MaxValue) < now;
}
