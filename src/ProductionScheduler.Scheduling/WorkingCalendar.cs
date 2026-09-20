namespace ProductionScheduler.Scheduling;

// A break inside the working day, as time-of-day offsets (§8.6: break windows are unavailable time).
public readonly record struct BreakWindow(TimeSpan Start, TimeSpan End);

// One contiguous stretch of a job's work on one calendar day — what the Calendar actually draws.
public readonly record struct WorkSegment(DateOnly Day, DateTime Start, DateTime End, double Eqa);

// Shared working-calendar math: given the shift window and working days from Settings (§6.2) and the
// break windows from the active shifts, advances a point in time to the next valid working moment, or
// places a duration / an amount of EqA work across it.
public static class WorkingCalendar
{
    public static DateTime AdvanceToWorkingWindow(
        DateTime t, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null)
    {
        while (true)
        {
            if (!workingDays.Contains((int)t.DayOfWeek))
            {
                t = t.Date.AddDays(1).AddHours(shiftStartHour);
                continue;
            }

            var dayStart = t.Date.AddHours(shiftStartHour);
            var dayEnd = t.Date.AddHours(shiftEndHour);

            if (t < dayStart)
            {
                t = dayStart;
                continue;
            }

            if (t >= dayEnd)
            {
                t = t.Date.AddDays(1).AddHours(shiftStartHour);
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

    // The next moment work must pause: the start of the next break today, or the end of the shift.
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

    // Places `eqa` units of work (each taking `minsPerEqa` minutes) starting at `start`, honouring the
    // shift window, working days, breaks and a per-day EqA capacity. `usedEqa` tracks what each calendar
    // day has already consumed on this station, so consecutive jobs share the same daily budget.
    // A null cap means "no limit" for that day. When `segments` is supplied it receives one entry per
    // stretch of work, which is how a multi-day job knows exactly where it is on each day.
    public static DateTime PlaceWork(
        DateTime start,
        double eqa,
        double minsPerEqa,
        int shiftStartHour,
        int shiftEndHour,
        HashSet<int> workingDays,
        Func<DateOnly, double?> dailyCapEqa,
        Dictionary<DateOnly, double> usedEqa,
        IReadOnlyList<BreakWindow>? breaks = null,
        List<WorkSegment>? segments = null)
    {
        var t = AdvanceToWorkingWindow(start, shiftStartHour, shiftEndHour, workingDays, breaks);
        if (eqa <= 0 || minsPerEqa <= 0)
        {
            return t;
        }

        var remaining = eqa;
        // The bound stops a station whose cap is zero on every future day from looping forever.
        for (var guard = 0; guard < 3650 && remaining > 1e-9; guard++)
        {
            var day = DateOnly.FromDateTime(t);
            var stop = NextStop(t, t.Date.AddHours(shiftEndHour), breaks);
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

            // Roll from the day we were filling, not from t: a shift ending at 24:00 leaves t on the NEXT midnight already.
            var capExhausted = cap is not null && cap.Value - usedEqa.GetValueOrDefault(day) <= 1e-9;
            t = capExhausted
                ? AdvanceToWorkingWindow(day.ToDateTime(TimeOnly.MinValue).AddDays(1).AddHours(shiftStartHour), shiftStartHour, shiftEndHour, workingDays, breaks)
                : AdvanceToWorkingWindow(stop, shiftStartHour, shiftEndHour, workingDays, breaks); // out of time before a break/shift end
        }

        return t;
    }

    public static DateTime PlaceDuration(
        DateTime start, double minutes, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays, IReadOnlyList<BreakWindow>? breaks = null)
    {
        var t = AdvanceToWorkingWindow(start, shiftStartHour, shiftEndHour, workingDays, breaks);
        var remaining = minutes;

        while (remaining > 0)
        {
            var stop = NextStop(t, t.Date.AddHours(shiftEndHour), breaks);
            var availableMinutes = (stop - t).TotalMinutes;

            if (remaining <= availableMinutes)
            {
                t = t.AddMinutes(remaining);
                remaining = 0;
            }
            else
            {
                remaining -= availableMinutes;
                t = AdvanceToWorkingWindow(stop, shiftStartHour, shiftEndHour, workingDays, breaks);
            }
        }

        return t;
    }
}
