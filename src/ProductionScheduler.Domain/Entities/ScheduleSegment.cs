namespace ProductionScheduler.Domain.Entities;

// One stretch of a scheduled job's work on one calendar day. ScheduleItem stays one row per job (with
// the job-level actuals the PRD reader maintains); the segments say where on each day the work is
// planned, which is what the Calendar draws and what lets a multi-day job appear on every day it runs.
public class ScheduleSegment
{
    public int Id { get; set; }
    public int ScheduleItemId { get; set; }
    public ScheduleItem ScheduleItem { get; set; } = null!;
    public DateOnly SegmentDate { get; set; }
    public DateTime SegmentStart { get; set; }
    public DateTime SegmentEnd { get; set; }
    public decimal PlannedEqA { get; set; }
}
