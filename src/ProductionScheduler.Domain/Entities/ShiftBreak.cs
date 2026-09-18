namespace ProductionScheduler.Domain.Entities;

public class ShiftBreak
{
    public int Id { get; set; }
    public int ShiftId { get; set; }
    public Shift Shift { get; set; } = null!;
    public TimeOnly BreakStart { get; set; }
    public int BreakDurationMins { get; set; }
}
