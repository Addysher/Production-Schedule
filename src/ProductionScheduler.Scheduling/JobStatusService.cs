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

    // Dashboard "Jobs at risk": the gap between the schedule's own estimated completion and the
    // customer's booked delivery date has shrunk to (or below) the configured buffer — a warning that
    // there's little slack left, before it actually slips past the due date (which is IsLate's job).
    // Excludes a job that's already late so the two Dashboard tiles don't double-count the same job.
    // The gap must also be non-negative: a schedule whose estimated completion already falls AFTER the
    // due date isn't merely "tight", it's already projected to miss it — that's worse than at-risk, not
    // a case of it, and a negative gap would otherwise trivially satisfy "<= bufferMinutes" by accident.
    public static bool IsAtRisk(DateTime? estimatedCompletionDate, DateOnly? dueDate, string status, DateTime now, int bufferMinutes)
    {
        if (status == "completed" || estimatedCompletionDate is null || dueDate is null || IsLate(dueDate, status, now))
        {
            return false;
        }

        var gapMinutes = (dueDate.Value.ToDateTime(TimeOnly.MaxValue) - estimatedCompletionDate.Value).TotalMinutes;
        return gapMinutes >= 0 && gapMinutes <= bufferMinutes;
    }

    // Dashboard "Jobs late" (production sense, distinct from IsLate's customer-due-date sense): the
    // schedule itself predicted this job would be done by now and it isn't — the factory has already
    // slipped against its own plan, which is an earlier and more actionable warning than waiting for
    // the customer's due date to pass.
    public static bool IsBehindSchedule(DateTime? estimatedCompletionDate, string status, DateTime now) =>
        status != "completed" && estimatedCompletionDate is not null && estimatedCompletionDate.Value < now;
}
