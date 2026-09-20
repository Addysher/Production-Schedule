namespace ProductionScheduler.Scheduling;

// Shared working-calendar math: given the shift window and working days from Settings (§6.2),
// advances a point in time to the next valid working moment, or places a duration across it.
public static class WorkingCalendar
{
    public static DateTime AdvanceToWorkingWindow(DateTime t, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays)
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

            return t;
        }
    }

    // Places `eqa` units of work (each taking `minsPerEqa` minutes) starting at `start`, honouring the
    // shift window, working days and a per-day EqA capacity. `usedEqa` tracks what each calendar day
    // has already consumed on this station, so consecutive jobs share the same daily budget.
    // A null cap means "no limit" for that day.
    public static DateTime PlaceWork(
        DateTime start,
        double eqa,
        double minsPerEqa,
        int shiftStartHour,
        int shiftEndHour,
        HashSet<int> workingDays,
        Func<DateOnly, double?> dailyCapEqa,
        Dictionary<DateOnly, double> usedEqa)
    {
        var t = AdvanceToWorkingWindow(start, shiftStartHour, shiftEndHour, workingDays);
        if (eqa <= 0 || minsPerEqa <= 0)
        {
            return t;
        }

        var remaining = eqa;
        // The bound stops a station whose cap is zero on every future day from looping forever.
        for (var guard = 0; guard < 3650 && remaining > 1e-9; guard++)
        {
            var day = DateOnly.FromDateTime(t);
            var minutesLeftInShift = (t.Date.AddHours(shiftEndHour) - t).TotalMinutes;
            var eqaByTime = minutesLeftInShift / minsPerEqa;
            var cap = dailyCapEqa(day);
            var eqaByCap = cap is null ? double.PositiveInfinity : Math.Max(cap.Value - usedEqa.GetValueOrDefault(day), 0);

            var place = Math.Min(remaining, Math.Min(eqaByTime, eqaByCap));
            if (place > 1e-9)
            {
                t = t.AddMinutes(place * minsPerEqa);
                usedEqa[day] = usedEqa.GetValueOrDefault(day) + place;
                remaining -= place;
                if (remaining <= 1e-9)
                {
                    break;
                }
            }

            // Out of shift time or out of capacity for this day — carry on the next working day.
            // Roll from the day we were filling, not from t: a shift ending at 24:00 leaves t on the NEXT midnight already.
            t = AdvanceToWorkingWindow(day.ToDateTime(TimeOnly.MinValue).AddDays(1).AddHours(shiftStartHour), shiftStartHour, shiftEndHour, workingDays);
        }

        return t;
    }

    public static DateTime PlaceDuration(DateTime start, double minutes, int shiftStartHour, int shiftEndHour, HashSet<int> workingDays)
    {
        var t = AdvanceToWorkingWindow(start, shiftStartHour, shiftEndHour, workingDays);
        var remaining = minutes;

        while (remaining > 0)
        {
            var dayEnd = t.Date.AddHours(shiftEndHour);
            var availableToday = (dayEnd - t).TotalMinutes;

            if (remaining <= availableToday)
            {
                t = t.AddMinutes(remaining);
                remaining = 0;
            }
            else
            {
                remaining -= availableToday;
                t = AdvanceToWorkingWindow(t.Date.AddDays(1).AddHours(shiftStartHour), shiftStartHour, shiftEndHour, workingDays);
            }
        }

        return t;
    }
}
