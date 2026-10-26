namespace ProductionScheduler.Domain.Entities;

// A one-off change to one line's working day (v3.0): overtime (a different start and/or end time, which
// can also open a normally non-working day) or the line closed (shutdown, public holiday). Breaks still
// come from the shifts. One row per line per day.
public class StationCapacityOverride
{
    public int Id { get; set; }
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public DateOnly OverrideDate { get; set; }
    public bool IsClosed { get; set; }
    public TimeOnly? StartTime { get; set; } // NULL = the normal start
    public TimeOnly? EndTime { get; set; }   // NULL = the normal end; 00:00 = midnight
    public string? Note { get; set; }        // e.g. "Overtime", "Easter Monday"
    public DateTime CreatedAt { get; set; }
}
