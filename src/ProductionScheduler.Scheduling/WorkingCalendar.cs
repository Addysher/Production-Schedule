namespace ProductionScheduler.Scheduling;

// A break inside the working day, as time-of-day offsets (§8.6: break windows are unavailable time).
public readonly record struct BreakWindow(TimeSpan Start, TimeSpan End);

// One day's working hours, as time-of-day offsets (End may be 24:00, midnight).
public readonly record struct DayWindow(TimeSpan Start, TimeSpan End);

// One contiguous stretch of a job's work on one calendar day — what the Calendar actually draws.
public readonly record struct WorkSegment(DateOnly Day, DateTime Start, DateTime End, double Eqa);

// Shared working-calendar math. The core takes `windowFor`: each day's working hours, or null for a day
// with no work (a non-working day, or a line closed for a shutdown or public holiday). That is how one
// line can work overtime or be closed on a given day (WorkCalendarSettings.WindowFor). Breaks apply
// inside every window. The fixed-hours overloads (one window on every working day) remain for callers
// that don't need per-day changes.
public static class WorkingCalendar
{
    // A calendar with no working time this far ahead is a setup error (e.g. breaks covering the whole day,
    // or a line closed for months), so placement gives up rather than looping forever.
    private const int SearchDays = 120;

    public static Func<DateOnly, DayWindow?> FixedWindow(TimeSpan dayStart, TimeSpan dayEnd, HashSet<int> workingDays) =>
        day => workingDays.Contains((int)day.DayOfWeek) ? new DayWindow(dayStart, dayEnd) : null;

    // --- Fixed-hours overloads ---

    public static DateTime AdvanceToWorkingWindow(
        DateTime t, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null) =>
        AdvanceToWorkingWindow(t, FixedWindow(TimeSpan.FromHours(shiftStartHour), TimeSpan.FromHours(shiftEndHour), workingDays), breaks);

    public static DateTime AdvanceToWorkingWindow(
        DateTime t, TimeSpan dayStart, TimeSpan dayEnd, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null) =>
        AdvanceToWorkingWindow(t, FixedWindow(dayStart, dayEnd, workingDays), breaks);

    public static DateTime PlaceWork(
        DateTime start, double eqa, double minsPerEqa, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays,
        Func<DateOnly, double?> dailyCapEqa, Dictionary<DateOnly, double> usedEqa, IReadOnlyList<BreakWindow>? breaks = null, List<WorkSegment>? segments = null) =>
        PlaceWork(start, eqa, minsPerEqa, FixedWindow(TimeSpan.FromHours(shiftStartHour), TimeSpan.FromHours(shiftEndHour), workingDays), dailyCapEqa, usedEqa, breaks, segments);

    public static DateTime PlaceWork(
        DateTime start, double eqa, double minsPerEqa, TimeSpan dayStart, TimeSpan dayEnd, HashSet<int> workingDays,
        Func<DateOnly, double?> dailyCapEqa, Dictionary<DateOnly, double> usedEqa, IReadOnlyList<BreakWindow>? breaks = null, List<WorkSegment>? segments = null) =>
        PlaceWork(start, eqa, minsPerEqa, FixedWindow(dayStart, dayEnd, workingDays), dailyCapEqa, usedEqa, breaks, segments);

    public static DateTime PlaceDuration(
        DateTime start, double minutes, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null) =>
        PlaceDuration(start, minutes, FixedWindow(TimeSpan.FromHours(shiftStartHour), TimeSpan.FromHours(shiftEndHour), workingDays), breaks);

    public static DateTime PlaceDuration(
        DateTime start, double minutes, TimeSpan dayStart, TimeSpan dayEnd, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null) =>
        PlaceDuration(start, minutes, FixedWindow(dayStart, dayEnd, workingDays), breaks);

    // --- Core ---

    // The next moment at or after t when work can happen.
    public static DateTime AdvanceToWorkingWindow(DateTime t, Func<DateOnly, DayWindow?> windowFor, IReadOnlyList<BreakWindow>? breaks = null)
    {
        var giveUpAt = t.AddDays(SearchDays);
        while (true)
        {
            if (t > giveUpAt)
            {
                throw new InvalidOperationException(
                    $"The working calendar has no working time in the next {SearchDays} days — check Working days, the shifts and their breaks, and any line closures.");
            }

            var window = windowFor(DateOnly.FromDateTime(t));
            if (window is not DayWindow w)
            {
                t = t.Date.AddDays(1);
                continue;
            }

            var windowStart = t.Date + w.Start;
            var windowEnd = t.Date + w.End;

            if (t < windowStart)
            {
                t = windowStart;
                continue;
            }

            if (t >= windowEnd)
            {
                t = t.Date.AddDays(1);
                continue;
            }

            if (breaks is { Count: > 0 })
            {
                var timeOfDay = t.TimeOfDay;
                var breakEnd = breaks.Where(b => b.Start <= timeOfDay && timeOfDay < b.End).Select(b => b.End).DefaultIfEmpty(TimeSpan.Zero).Max();
                if (breakEnd > TimeSpan.Zero)
                {
                    t = t.Date + breakEnd; // in a break: work resumes when it ends
                    continue;
                }
            }

            return t;
        }
    }

    // The end of the working window t is in (t must already be at a working moment).
    private static DateTime WindowEnd(DateTime t, Func<DateOnly, DayWindow?> windowFor) =>
        t.Date + (windowFor(DateOnly.FromDateTime(t))?.End ?? t.TimeOfDay);

    // The next moment work must pause: the start of the next break today, or the end of the window.
    private static DateTime NextStop(DateTime t, DateTime dayEnd, IReadOnlyList<BreakWindow>? breaks)
    {
        if (breaks is not { Count: > 0 })
        {
            return dayEnd;
        }

        var timeOfDay = t.TimeOfDay;
        var upcoming = breaks.Where(b => b.Start > timeOfDay).Select(b => b.Start).DefaultIfEmpty(TimeSpan.MaxValue).Min();
        if (upcoming == TimeSpan.MaxValue)
        {
            return dayEnd;
        }

        var stop = t.Date + upcoming;
        return stop < dayEnd ? stop : dayEnd;
    }

    // Places `eqa` units of work (each taking `minsPerEqa` minutes) starting at `start`, honouring each
    // day's window, breaks and an optional per-day EqA capacity. `usedEqa` tracks what each calendar day
    // has already consumed, so consecutive jobs share the same daily budget; a null cap means no limit.
    // When `segments` is supplied it receives one entry per stretch of work, which is how a multi-day job
    // knows exactly where it is on each day.
    public static DateTime PlaceWork(
        DateTime start,
        double eqa,
        double minsPerEqa,
        Func<DateOnly, DayWindow?> windowFor,
        Func<DateOnly, double?> dailyCapEqa,
        Dictionary<DateOnly, double> usedEqa,
        IReadOnlyList<BreakWindow>? breaks = null,
        List<WorkSegment>? segments = null)
    {
        var t = AdvanceToWorkingWindow(start, windowFor, breaks);
        if (eqa <= 0 || minsPerEqa <= 0)
        {
            return t;
        }

        var remaining = eqa;
        // The bound stops a line whose cap is zero on every future day from looping forever.
        for (var guard = 0; guard < 3650 && remaining > 1e-9; guard++)
        {
            var day = DateOnly.FromDateTime(t);
            var stop = NextStop(t, WindowEnd(t, windowFor), breaks);
            var eqaByTime = (stop - t).TotalMinutes / minsPerEqa;
            var cap = dailyCapEqa(day);
            var eqaByCap = cap is null ? double.PositiveInfinity : Math.Max(cap.Value - usedEqa.GetValueOrDefault(day), 0);

            var place = Math.Min(remaining, Math.Min(eqaByTime, eqaByCap));
            if (place > 1e-9)
            {
                var segmentStart = t;
                t = t.AddMinutes(place * minsPerEqa);
                usedEqa[day] = usedEqa.GetValueOrDefault(day) + place;
                remaining -= place;
                segments?.Add(new WorkSegment(day, segmentStart, t, place));
                if (remaining <= 1e-9)
                {
                    break;
                }
            }

            // Roll from the day we were filling, not from t: a window ending at 24:00 leaves t on the NEXT midnight already.
            var capExhausted = cap is not null && cap.Value - usedEqa.GetValueOrDefault(day) <= 1e-9;
            t = capExhausted
                ? AdvanceToWorkingWindow(day.ToDateTime(TimeOnly.MinValue).AddDays(1), windowFor, breaks)
                : AdvanceToWorkingWindow(stop, windowFor, breaks); // out of time before a break/window end
        }

        return t;
    }

    public static DateTime PlaceDuration(DateTime start, double minutes, Func<DateOnly, DayWindow?> windowFor, IReadOnlyList<BreakWindow>? breaks = null)
    {
        var t = AdvanceToWorkingWindow(start, windowFor, breaks);
        var remaining = minutes;

        while (remaining > 0)
        {
            var stop = NextStop(t, WindowEnd(t, windowFor), breaks);
            var availableMinutes = (stop - t).TotalMinutes;

            if (remaining <= availableMinutes)
            {
                t = t.AddMinutes(remaining);
                remaining = 0;
            }
            else
            {
                remaining -= availableMinutes;
                t = AdvanceToWorkingWindow(stop, windowFor, breaks);
            }
        }

        return t;
    }

    // Working minutes in one day's window, after breaks.
    public static double NetMinutes(DayWindow? window, IReadOnlyList<BreakWindow> breaks)
    {
        if (window is not DayWindow w || w.End <= w.Start)
        {
            return 0;
        }

        var lost = breaks.Sum(b => Math.Max(0, (Min(b.End, w.End) - Max(b.Start, w.Start)).TotalMinutes));
        return Math.Max(0, (w.End - w.Start).TotalMinutes - lost);

        static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
        static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
    }
}
