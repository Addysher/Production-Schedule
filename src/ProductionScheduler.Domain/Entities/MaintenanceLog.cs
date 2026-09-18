namespace ProductionScheduler.Domain.Entities;

public class MaintenanceLog
{
    public int Id { get; set; }
    public string TaskName { get; set; } = null!; // 'MpsToDatIn' | 'SchDelete' | 'MpsArchive' | 'SawFilesCleanup'
    public string? JobRef { get; set; }
    public string? SourcePath { get; set; }
    public string? DestPath { get; set; }
    public string Outcome { get; set; } = null!; // 'success' | 'skipped' | 'error'
    public string? Message { get; set; }
    public DateTime RunAt { get; set; }

    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
