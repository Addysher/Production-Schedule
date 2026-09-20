using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling;

// Everything WorkingCalendar needs, loaded once so the scheduler, the Quote Estimator and the Calendar
// screen all describe the same working day.
//
// The working window and working days come from the Settings keys (§6.2). Breaks come from the active
// shifts (§8.6), as the union of all their break windows: the schema supports several shifts per day,
// but the factory runs one shared window, so a break is simply "a time of day nothing runs".
// A shift's own start/end time is not used to shape the window — the Settings hours are the authority.
public sealed record WorkCalendarSettings(
    int ShiftStartHour, int ShiftEndHour, HashSet<int> WorkingDays, IReadOnlyList<BreakWindow> Breaks)
{
    public static async Task<WorkCalendarSettings> LoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        var workingDays = settings.GetValueOrDefault("WorkingDays", "1,2,3,4,5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToHashSet();

        var breaks = (await db.ShiftBreaks.AsNoTracking().Where(b => b.Shift.IsActive).ToListAsync(ct))
            .Select(b =>
            {
                var start = b.BreakStart.ToTimeSpan();
                var end = start + TimeSpan.FromMinutes(b.BreakDurationMins);
                return new BreakWindow(start, end > TimeSpan.FromHours(24) ? TimeSpan.FromHours(24) : end);
            })
            .Where(b => b.End > b.Start)
            .OrderBy(b => b.Start)
            .ToList();

        return new WorkCalendarSettings(
            int.Parse(settings.GetValueOrDefault("ShiftStartHour", "6")),
            int.Parse(settings.GetValueOrDefault("ShiftEndHour", "18")),
            workingDays,
            breaks);
    }
}
