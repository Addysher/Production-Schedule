namespace ProductionScheduler.Domain.Entities;

public class Shift
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ShiftBreak> Breaks { get; set; } = new List<ShiftBreak>();
    public ICollection<Operator> Operators { get; set; } = new List<Operator>();
}
