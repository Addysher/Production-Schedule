namespace ProductionScheduler.Domain.Entities;

public class JobType
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? ComponentType { get; set; } // links to Stations.ComponentType group
    public decimal MinsPerUnit { get; set; }
    public bool IsActive { get; set; } = true;
}
