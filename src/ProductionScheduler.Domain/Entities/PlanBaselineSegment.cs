namespace ProductionScheduler.Domain.Entities;

// Today's plan as it stood, per line (v3.0 feedback loop). The live plan moves with every re-plan — a job
// that was due to run this morning and didn't start is simply re-placed later — so "planned by now" is
// measured against this record instead: a re-plan only rewrites the part of the day still ahead.
public class PlanBaselineSegment
{
    public int Id { get; set; }
    public int StationId { get; set; }
    public string JobRef { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public decimal PlannedEqA { get; set; }
}
