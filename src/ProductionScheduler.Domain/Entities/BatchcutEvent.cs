namespace ProductionScheduler.Domain.Entities;

public class BatchcutEvent
{
    public int Id { get; set; }
    public int BatchcutPathId { get; set; }
    public BatchcutWatchPath BatchcutPath { get; set; } = null!;
    public string FolderName { get; set; } = null!; // e.g. '59778MWF__DONE'
    public string? JobRef { get; set; }
    public DateTime DetectedAt { get; set; }
    public string Status { get; set; } = null!; // 'applied' | 'error' | 'duplicate'
    public string? ErrorMessage { get; set; }

    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
