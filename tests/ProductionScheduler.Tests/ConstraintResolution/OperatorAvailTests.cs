using ProductionScheduler.Scheduling.ConstraintResolution;
using Xunit;

namespace ProductionScheduler.Tests.ConstraintResolution;

public class OperatorAvailTests
{
    private static readonly TimeOnly ShiftStart = new(6, 0);
    private static readonly TimeOnly ShiftEnd = new(18, 0);

    [Fact]
    public void Validate_WithinShiftAndNoBreakOverlap_IsAvailable()
    {
        var start = new DateTime(2026, 9, 21, 8, 0, 0);
        var end = new DateTime(2026, 9, 21, 9, 0, 0);
        var breaks = new[] { (new TimeOnly(12, 0), 30) };

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, breaks);

        Assert.True(result.IsAvailable);
        Assert.Empty(result.ConflictReasons);
    }

    [Fact]
    public void Validate_NoBreaksConfigured_StillValidatesShiftWindow()
    {
        var start = new DateTime(2026, 9, 21, 8, 0, 0);
        var end = new DateTime(2026, 9, 21, 9, 0, 0);

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, Array.Empty<(TimeOnly, int)>());

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public void Validate_JobSpansABreak_IsUnavailable()
    {
        // Break is 12:00-12:30; job runs 11:45-12:45, spanning across it.
        var start = new DateTime(2026, 9, 21, 11, 45, 0);
        var end = new DateTime(2026, 9, 21, 12, 45, 0);
        var breaks = new[] { (new TimeOnly(12, 0), 30) };

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, breaks);

        Assert.False(result.IsAvailable);
        Assert.Contains(result.ConflictReasons, r => r.Contains("overlaps break"));
    }

    [Fact]
    public void Validate_JobStartsInsideABreak_IsUnavailable()
    {
        var start = new DateTime(2026, 9, 21, 12, 10, 0);
        var end = new DateTime(2026, 9, 21, 13, 0, 0);
        var breaks = new[] { (new TimeOnly(12, 0), 30) };

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, breaks);

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Validate_OutsideShiftWindow_IsUnavailable()
    {
        var start = new DateTime(2026, 9, 21, 5, 0, 0);
        var end = new DateTime(2026, 9, 21, 7, 0, 0);

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, Array.Empty<(TimeOnly, int)>());

        Assert.False(result.IsAvailable);
        Assert.Contains(result.ConflictReasons, r => r.Contains("outside the shift window"));
    }

    [Fact]
    public void Validate_EndBeforeStart_IsUnavailable()
    {
        var start = new DateTime(2026, 9, 21, 9, 0, 0);
        var end = new DateTime(2026, 9, 21, 8, 0, 0);

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, Array.Empty<(TimeOnly, int)>());

        Assert.False(result.IsAvailable);
    }

    [Fact]
    public void Validate_SpansMultipleCalendarDays_IsUnavailable()
    {
        var start = new DateTime(2026, 9, 21, 17, 0, 0);
        var end = new DateTime(2026, 9, 22, 7, 0, 0);

        var result = OperatorAvail.Validate(start, end, ShiftStart, ShiftEnd, Array.Empty<(TimeOnly, int)>());

        Assert.False(result.IsAvailable);
        Assert.Contains(result.ConflictReasons, r => r.Contains("more than one calendar day"));
    }
}
