namespace ProductionScheduler.Domain.Entities;

public class ScheduleItem
{
    public int Id { get; set; }
    public string JobRef { get; set; } = null!;
    public Job Job { get; set; } = null!;
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public int? OperatorId { get; set; }
    public Operator? Operator { get; set; }
    public DateOnly ScheduleDate { get; set; }

    // Planned (set by scheduler)
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }

    // Freeze window (§5.20): where a frozen job was planned to start when it was last planned normally. Each
    // run re-times it from here, so a temporary push (a break added then removed) never sticks.
    public DateTime? FrozenAnchorStart { get; set; }
    public decimal PlannedEqA { get; set; }
    public int MembersTotal { get; set; }

    // Actuals (updated by PRD reader)
    public DateTime? ActualStart { get; set; }
    public DateTime? ActualEnd { get; set; }
    public decimal EqaCompleted { get; set; }
    public int MembersCompleted { get; set; }

    // Job creep tracking
    public bool CreepDetected { get; set; }
    public DateTime? CreepDetectedAt { get; set; }
    public DateTime? CreepApprovedAt { get; set; }
    public int? CreepApprovedByUserId { get; set; }
    public User? CreepApprovedByUser { get; set; }

    public ICollection<ScheduleSegment> Segments { get; set; } = new List<ScheduleSegment>();

    public DateTime GeneratedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
