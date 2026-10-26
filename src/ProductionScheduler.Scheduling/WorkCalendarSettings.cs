using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling;

// Everything WorkingCalendar needs, loaded once so the scheduler, the Quote Estimator and the Calendar
// screen all describe the same working day.
//
// The working day comes from the active shifts (§8.6): from the earliest shift start to the latest
// shift end, to the minute (e.g. 06:00–14:30). Only when no shift is active do the Settings hours
// (ShiftStartHour / ShiftEndHour, §6.2) apply. Breaks are the union of the active shifts' breaks: the
// factory runs one shared day, so a break is simply "a time of day nothing runs". Working days come
// from Settings.
public sealed record WorkCalendarSettings(
    TimeSpan DayStart, TimeSpan DayEnd, HashSet<int> WorkingDays, IReadOnlyList<BreakWindow> Breaks, int FreezeWindowDays = 0)
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

    // v3.0 delivery window (Settings → ProductionLeadDaysMin / Max, working days): a job should be finished
    // at least LeadDaysMin working days before its delivery, and not started more than LeadDaysMax working
    // days before it (0 = no limit), so finished work doesn't fill the yard.
    public int LeadDaysMin { get; init; }
    public int LeadDaysMax { get; init; }

    // v3.0: jobs with no booked delivery date (TBA, including quotes) are planned as pencilled-in work.
    public bool PencilTbaJobs { get; init; } = true;

    // The working day as most lines see it, before any one-off change for a line.
    public DayWindow? NormalWindow(DateOnly day) => WorkingDays.Contains((int)day.DayOfWeek) ? new DayWindow(DayStart, DayEnd) : null;

    // One line's working hours on each day: its one-off changes (overtime hours, or closed) over the normal
    // day. Overtime on a normally non-working day opens it, with the normal hours unless times are given.
    public Func<DateOnly, DayWindow?> WindowFor(IReadOnlyDictionary<DateOnly, StationCapacityOverride>? overrides) => day =>
    {
        if (overrides is null || !overrides.TryGetValue(day, out var change))
        {
            return NormalWindow(day);
        }

        if (change.IsClosed)
        {
            return null;
        }

        var start = change.StartTime?.ToTimeSpan() ?? DayStart;
        var end = change.EndTime is { } e ? (e == TimeOnly.MinValue ? TimeSpan.FromHours(24) : e.ToTimeSpan()) : DayEnd;
        return end > start ? new DayWindow(start, end) : null;
    };

    // What a line can make on a day: its EqA per hour × that day's working hours after breaks.
    public double DailyTargetEqa(double eqaPerHour, DayWindow? window) => eqaPerHour * WorkingCalendar.NetMinutes(window, Breaks) / 60;

    // Steps back `count` working days from `day` (0 = the day itself).
    public DateOnly WorkingDaysBefore(DateOnly day, int count)
    {
        for (var counted = 0; counted < count && WorkingDays.Count > 0;)
        {
            day = day.AddDays(-1);
            if (WorkingDays.Contains((int)day.DayOfWeek))
            {
                counted++;
            }
        }

        return day;
    }

    // The latest a job due on `due` should finish: the end of the day LeadDaysMin working days before it.
    public DateTime LatestFinish(DateOnly due) => WorkingDaysBefore(due, LeadDaysMin).ToDateTime(TimeOnly.MinValue).AddDays(1);

    // The earliest it may start: the start of the day LeadDaysMax working days before it (null = no limit).
    public DateTime? EarliestStart(DateOnly due) => LeadDaysMax > 0 ? WorkingDaysBefore(due, LeadDaysMax).ToDateTime(TimeOnly.MinValue) : null;

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

        var (dayStart, dayEnd) = (TimeSpan.FromHours(shiftStart), TimeSpan.FromHours(shiftEnd));

        // A shift ending at 00:00 ends at midnight. Shifts that run past midnight aren't supported (the
        // shift form refuses them), so one found in the database is ignored rather than breaking the day.
        var shifts = (await db.Shifts.AsNoTracking().Where(sh => sh.IsActive).ToListAsync(ct))
            .Select(sh => (Start: sh.StartTime.ToTimeSpan(), End: sh.EndTime == TimeOnly.MinValue ? TimeSpan.FromHours(24) : sh.EndTime.ToTimeSpan()))
            .Where(sh => sh.End > sh.Start)
            .ToList();
        if (shifts.Count > 0)
        {
            (dayStart, dayEnd) = (shifts.Min(sh => sh.Start), shifts.Max(sh => sh.End));
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

        var leadMin = Math.Clamp(Int("ProductionLeadDaysMin", 0), 0, 30);
        var leadMax = Math.Clamp(Int("ProductionLeadDaysMax", 0), 0, 60);
        return new WorkCalendarSettings(dayStart, dayEnd, workingDays, breaks, Math.Clamp(Int("FreezeWindowDays", 0), 0, 30))
        {
            LeadDaysMin = leadMin,
            LeadDaysMax = leadMax > 0 && leadMax < leadMin ? leadMin : leadMax,
            PencilTbaJobs = !string.Equals(settings.GetValueOrDefault("PencilTbaJobs", "true"), "false", StringComparison.OrdinalIgnoreCase),
        };
    }
}
