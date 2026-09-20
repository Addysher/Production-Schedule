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

    // ---- breaks (§8.6): break windows are unavailable time; work pauses and resumes around them ----

    private static readonly IReadOnlyList<BreakWindow> Lunch = new[] { new BreakWindow(TimeSpan.FromHours(12), TimeSpan.FromHours(13)) };

    [Fact]
    public void PlaceDuration_PausesForABreak_AndResumesAfterIt()
    {
        // 11:00 + 2h of work with a 12:00-13:00 break: one hour before it, one hour after -> 14:00.
        var end = WorkingCalendar.PlaceDuration(new DateTime(2026, 9, 21, 11, 0, 0), 120, 6, 18, AllDays, Lunch);

        Assert.Equal(new DateTime(2026, 9, 21, 14, 0, 0), end);
    }

    [Fact]
    public void PlaceWork_RecordsOneSegmentEachSideOfABreak()
    {
        var segments = new List<WorkSegment>();

        var end = WorkingCalendar.PlaceWork(new DateTime(2026, 9, 21, 11, 0, 0), 120, 1, 6, 18, AllDays, NoCap,
            new Dictionary<DateOnly, double>(), Lunch, segments);

        Assert.Equal(new DateTime(2026, 9, 21, 14, 0, 0), end);
        Assert.Collection(segments,
            s => { Assert.Equal(new DateTime(2026, 9, 21, 11, 0, 0), s.Start); Assert.Equal(new DateTime(2026, 9, 21, 12, 0, 0), s.End); },
            s => { Assert.Equal(new DateTime(2026, 9, 21, 13, 0, 0), s.Start); Assert.Equal(new DateTime(2026, 9, 21, 14, 0, 0), s.End); });
        Assert.Equal(120, segments.Sum(s => s.Eqa), 6);
    }

    [Fact]
    public void AdvanceToWorkingWindow_StartingInsideABreak_MovesToTheEndOfIt()
    {
        var start = WorkingCalendar.AdvanceToWorkingWindow(new DateTime(2026, 9, 21, 12, 20, 0), 6, 18, AllDays, Lunch);

        Assert.Equal(new DateTime(2026, 9, 21, 13, 0, 0), start);
    }

    [Fact]
    public void PlaceDuration_WorkThatEndsExactlyWhenABreakStarts_EndsThere()
    {
        var end = WorkingCalendar.PlaceDuration(new DateTime(2026, 9, 21, 11, 0, 0), 60, 6, 18, AllDays, Lunch);

        Assert.Equal(new DateTime(2026, 9, 21, 12, 0, 0), end);
        // ...and the next job starts after the break, not during it.
        Assert.Equal(new DateTime(2026, 9, 21, 13, 0, 0), WorkingCalendar.AdvanceToWorkingWindow(end, 6, 18, AllDays, Lunch));
    }

    [Fact]
    public void PlaceDuration_SeveralBreaksInOneDay_AreAllSkipped()
    {
        var breaks = new[]
        {
            new BreakWindow(TimeSpan.FromHours(10), TimeSpan.FromHours(10.25)),
            new BreakWindow(TimeSpan.FromHours(12), TimeSpan.FromHours(12.5)),
        };

        // 09:00 + 5h: 09:00-10:00 (60), 10:15-12:00 (105), then 135 more from 12:30 -> 14:45.
        var end = WorkingCalendar.PlaceDuration(new DateTime(2026, 9, 21, 9, 0, 0), 300, 6, 18, AllDays, breaks);

        Assert.Equal(new DateTime(2026, 9, 21, 14, 45, 0), end);
    }

    [Fact]
    public void PlaceDuration_ABreakAtTheEndOfTheShift_CarriesTheRestToTomorrow()
    {
        var lastHourBreak = new[] { new BreakWindow(TimeSpan.FromHours(17), TimeSpan.FromHours(18)) };

        // 16:30 + 1h: 30 min before the 17:00 break, and the shift ends when the break does -> 06:30 tomorrow.
        var end = WorkingCalendar.PlaceDuration(new DateTime(2026, 9, 21, 16, 30, 0), 60, 6, 18, AllDays, lastHourBreak);

        Assert.Equal(new DateTime(2026, 9, 22, 6, 30, 0), end);
    }

    // ---- segments ----

    [Fact]
    public void PlaceWork_MultiDayJobUnderADailyCap_HasOneSegmentPerDay()
    {
        var segments = new List<WorkSegment>();

        WorkingCalendar.PlaceWork(new DateTime(2026, 9, 21, 6, 0, 0), 250, 1, 6, 18, AllDays, _ => 100, new Dictionary<DateOnly, double>(), null, segments);

        Assert.Equal(new[] { 100.0, 100.0, 50.0 }, segments.Select(s => s.Eqa).ToArray());
        Assert.Equal(new[] { 21, 22, 23 }, segments.Select(s => s.Day.Day).ToArray());
        // The cap leaves the rest of each day empty — segments show only the work, not the whole span.
        Assert.All(segments.Take(2), s => Assert.Equal(TimeSpan.FromMinutes(100), s.End - s.Start));
    }
}
