namespace ProductionScheduler.Domain.Entities;

public class BatchcutWatchPath
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string UncPath { get; set; } = null!;
    public bool IsActive { get; set; } = true;

    public ICollection<BatchcutEvent> Events { get; set; } = new List<BatchcutEvent>();
}
