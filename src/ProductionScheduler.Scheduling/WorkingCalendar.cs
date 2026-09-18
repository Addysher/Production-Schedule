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
