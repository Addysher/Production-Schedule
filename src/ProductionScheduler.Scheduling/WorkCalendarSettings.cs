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
    int ShiftStartHour, int ShiftEndHour, HashSet<int> WorkingDays, IReadOnlyList<BreakWindow> Breaks, int FreezeWindowDays = 0)
{
    // The freeze (lock-out) window: today through the FreezeWindowDays-th working day after today.
    // Not-yet-started work planned to start before this moment doesn't move (see FreezeWindow).
    // NULL when the window is off (FreezeWindowDays = 0).
    public DateTime? FreezeUntil(DateTime now)
    {
        if (FreezeWindowDays <= 0 || WorkingDays.Count == 0)
        {
            return null;
        }

        var day = now.Date;
        for (var counted = 0; counted < FreezeWindowDays;)
        {
            day = day.AddDays(1);
            if (WorkingDays.Contains((int)day.DayOfWeek))
            {
                counted++;
            }
        }

        return day.AddDays(1); // the end of that working day
    }

    public static async Task<WorkCalendarSettings> LoadAsync(AppDbContext db, CancellationToken ct = default)
    {
        var settings = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        // Values are validated when saved, but a row edited directly in the database must not be able to
        // hang or crash scheduling: anything unusable falls back to the defaults.
        int Int(string key, int fallback) => int.TryParse(settings.GetValueOrDefault(key), out var v) ? v : fallback;

        var workingDays = settings.GetValueOrDefault("WorkingDays", "1,2,3,4,5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => int.TryParse(p, out var d) ? d : -1)
            .Where(d => d is >= 0 and <= 6)
            .ToHashSet();
        if (workingDays.Count == 0)
        {
            workingDays = [1, 2, 3, 4, 5];
        }

        var shiftStart = Int("ShiftStartHour", 6);
        var shiftEnd = Int("ShiftEndHour", 18);
        if (shiftStart is < 0 or > 23 || shiftEnd is < 1 or > 24 || shiftStart >= shiftEnd)
        {
            (shiftStart, shiftEnd) = (6, 18);
        }

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

        return new WorkCalendarSettings(shiftStart, shiftEnd, workingDays, breaks, Math.Clamp(Int("FreezeWindowDays", 0), 0, 30));
    }
}
