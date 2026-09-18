namespace ProductionScheduler.Domain.Entities;

public class Operator
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ScheduleItem> ScheduleItems { get; set; } = new List<ScheduleItem>();
}
