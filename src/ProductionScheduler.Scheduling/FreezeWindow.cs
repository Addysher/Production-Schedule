using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling;

// v3.0 freeze (lock-out) window: a job that hasn't started yet and is planned to start inside the
// window keeps its slot — Recalculate, imports, edits and drag-and-drop all leave it where it is, and
// nothing can be dropped into the window. FreezeWindowDays = 0 (Admin → System Settings) turns it off.
public static class FreezeWindow
{
    private static readonly string[] NotStarted = ["ready", "approved", "cut"];

    // Each frozen job's current (not-started) plan rows, with segments, keyed by job.
    public static async Task<Dictionary<string, List<ScheduleItem>>> FrozenPlansAsync(AppDbContext db, DateTime? freezeUntil, CancellationToken ct = default)
    {
        if (freezeUntil is not DateTime until)
        {
            return [];
        }

        var items = await db.ScheduleItems
            .Include(si => si.Segments)
            // Only called-up (booked) work is frozen: a TBA job can't be made yet, so it stays movable (v3.0).
            .Where(si => !si.IsArchived && si.ActualStart == null && !si.Job.IsArchived && NotStarted.Contains(si.Job.Status)
                && si.Job.BookedDeliveryDate != null)
            .AsNoTracking()
            .ToListAsync(ct);

        // A plan that has already run its whole course without the job starting is stale, not frozen —
        // it's re-planned from now like any other late job instead of being stuck in the past.
        var now = Domain.SiteClock.Now;
        return items
            .GroupBy(si => si.JobRef)
            .Where(g => g.Min(si => si.PlannedStart) < until && g.Max(si => si.PlannedEnd) > now)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    public static async Task<HashSet<string>> FrozenJobRefsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var calendar = await WorkCalendarSettings.LoadAsync(db, ct);
        return (await FrozenPlansAsync(db, calendar.FreezeUntil(Domain.SiteClock.Now), ct)).Keys.ToHashSet();
    }
}
