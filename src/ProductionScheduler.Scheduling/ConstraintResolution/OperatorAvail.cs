namespace ProductionScheduler.Scheduling.ConstraintResolution;

public record AvailabilityResult(bool IsAvailable, IReadOnlyList<string> ConflictReasons)
{
    public static AvailabilityResult Ok() => new(true, Array.Empty<string>());
    public static AvailabilityResult Fail(IReadOnlyList<string> reasons) => new(false, reasons);
}

// Validates a proposed (startTime, endTime) window against an operator's shift window and break periods.
// Per §8.6, break windows are unavailable time — a job may not start or span across one.
public static class OperatorAvail
{
    public static AvailabilityResult Validate(
        DateTime proposedStart,
        DateTime proposedEnd,
        TimeOnly shiftStart,
        TimeOnly shiftEnd,
        IEnumerable<(TimeOnly BreakStart, int BreakDurationMins)> breaks)
    {
        var reasons = new List<string>();

        if (proposedEnd <= proposedStart)
        {
            reasons.Add("Proposed end time must be after the start time.");
            return AvailabilityResult.Fail(reasons);
        }

        if (proposedStart.Date != proposedEnd.Date)
        {
            reasons.Add("Proposed window spans more than one calendar day.");
        }

        var startTod = TimeOnly.FromDateTime(proposedStart);
        var endTod = TimeOnly.FromDateTime(proposedEnd);

        if (startTod < shiftStart || endTod > shiftEnd)
        {
            reasons.Add($"Proposed window {startTod}-{endTod} falls outside the shift window {shiftStart}-{shiftEnd}.");
        }

        foreach (var (breakStart, breakDurationMins) in breaks)
        {
            var breakEnd = breakStart.AddMinutes(breakDurationMins);
            if (startTod < breakEnd && endTod > breakStart)
            {
                reasons.Add($"Proposed window overlaps break {breakStart}-{breakEnd}.");
            }
        }

        return reasons.Count == 0 ? AvailabilityResult.Ok() : AvailabilityResult.Fail(reasons);
    }
}
