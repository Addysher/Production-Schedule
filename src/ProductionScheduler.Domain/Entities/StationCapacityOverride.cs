namespace ProductionScheduler.Domain.Entities;

// Schema inferred from usage in §4.3 (IX_StationCapacityOverrides_Station_Date),
// §3.4 (StationsController capacity-overrides endpoints) and §5.15/§7 (override EqA target per day).
public class StationCapacityOverride
{
    public int Id { get; set; }
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public DateOnly OverrideDate { get; set; }
    public decimal EqATarget { get; set; }
    public DateTime CreatedAt { get; set; }
}
