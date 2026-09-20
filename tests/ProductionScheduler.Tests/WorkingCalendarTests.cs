using ProductionScheduler.Scheduling;
using Xunit;

namespace ProductionScheduler.Tests;

public class WorkingCalendarTests
{
    private static readonly HashSet<int> AllDays = new() { 0, 1, 2, 3, 4, 5, 6 };
    private static readonly HashSet<int> Weekdays = new() { 1, 2, 3, 4, 5 };
    private static readonly Func<DateOnly, double?> NoCap = _ => null;

    private static DateTime Place(DateTime start, double minutes, int shiftStart, int shiftEnd, HashSet<int> days) =>
        WorkingCalendar.PlaceWork(start, minutes, 1, shiftStart, shiftEnd, days, NoCap, new Dictionary<DateOnly, double>());

    [Fact]
    public void PlaceWork_ContinuousShift_JobCrossingMidnight_DoesNotGainAnExtraDay()
    {
        // 600 minutes from 18:30 ends at 04:30 the next morning — not a day later.
        var end = Place(new DateTime(2026, 9, 20, 18, 30, 0), 600, 0, 24, AllDays);

        Assert.Equal(new DateTime(2026, 9, 21, 4, 30, 0), end);
    }

    [Fact]
    public void PlaceWork_ShiftEndingAtMidnight_ContinuesFromTheNextShiftStart()
    {
        // 06:00-24:00 shift: 240 minutes remain tonight (20:00-24:00), the last 60 are 06:00-07:00 tomorrow.
        var end = Place(new DateTime(2026, 9, 21, 20, 0, 0), 300, 6, 24, AllDays);

        Assert.Equal(new DateTime(2026, 9, 22, 7, 0, 0), end);
    }

    [Fact]
    public void PlaceWork_OrdinaryDayShift_CarriesOverToTheNextMorning()
    {
        // 06:00-18:00: from 14:00, 4 hours fit today, the other 4 run 06:00-10:00 tomorrow.
        var end = Place(new DateTime(2026, 9, 21, 14, 0, 0), 480, 6, 18, AllDays);

        Assert.Equal(new DateTime(2026, 9, 22, 10, 0, 0), end);
    }

    [Fact]
    public void PlaceWork_SkipsTheWeekend()
    {
        // Friday 16:00, 4 hours of work on a 06:00-18:00 weekday shift: 2h Friday, 2h Monday morning.
        var end = Place(new DateTime(2026, 9, 25, 16, 0, 0), 240, 6, 18, Weekdays);

        Assert.Equal(new DateTime(2026, 9, 28, 8, 0, 0), end);
    }

    [Fact]
    public void PlaceWork_StartingOutsideTheShift_BeginsAtTheNextShiftStart()
    {
        // Saturday evening, weekday-only shift: nothing happens until Monday 06:00.
        var end = Place(new DateTime(2026, 9, 26, 20, 0, 0), 60, 6, 18, Weekdays);

        Assert.Equal(new DateTime(2026, 9, 28, 7, 0, 0), end);
    }

    [Fact]
    public void PlaceWork_DailyCap_IsSharedAcrossConsecutiveJobsOnTheSameDay()
    {
        var used = new Dictionary<DateOnly, double>();
        double? Cap(DateOnly _) => 100;
        var start = new DateTime(2026, 9, 20, 0, 0, 0);

        var firstEnd = WorkingCalendar.PlaceWork(start, 60, 1, 0, 24, AllDays, Cap, used);
        // Only 40 EqA of the day's 100 remain, so the second job's last 20 spill into tomorrow.
        var secondEnd = WorkingCalendar.PlaceWork(firstEnd, 60, 1, 0, 24, AllDays, Cap, used);

        Assert.Equal(new DateTime(2026, 9, 20, 1, 0, 0), firstEnd);
        Assert.Equal(new DateTime(2026, 9, 21, 0, 20, 0), secondEnd);
        Assert.Equal(100, used[new DateOnly(2026, 9, 20)], 6);
        Assert.Equal(20, used[new DateOnly(2026, 9, 21)], 6);
    }

    [Fact]
    public void PlaceWork_ZeroWork_EndsWhereItStarts()
    {
        var start = new DateTime(2026, 9, 21, 9, 0, 0);

        Assert.Equal(start, Place(start, 0, 6, 18, AllDays));
    }

    [Fact]
    public void PlaceWork_ACapOfZeroEveryDay_StillTerminates()
    {
        var end = WorkingCalendar.PlaceWork(new DateTime(2026, 9, 20, 6, 0, 0), 10, 1, 6, 18, AllDays, _ => 0, new Dictionary<DateOnly, double>());

        Assert.True(end > new DateTime(2026, 9, 20, 6, 0, 0)); // bounded search, no infinite loop
    }

    [Fact]
    public void PlaceDuration_AgreesWithPlaceWork_AcrossMidnight()
    {
        var start = new DateTime(2026, 9, 20, 18, 30, 0);

        Assert.Equal(Place(start, 600, 0, 24, AllDays), WorkingCalendar.PlaceDuration(start, 600, 0, 24, AllDays));
    }
}
