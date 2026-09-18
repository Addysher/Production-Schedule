namespace ProductionScheduler.Domain.Entities;

public class SetupTime
{
    public string FromComponentType { get; set; } = null!;
    public string ToComponentType { get; set; } = null!;
    public int DurationMins { get; set; }
}
