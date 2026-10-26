namespace ProductionScheduler.Domain.Entities;

public class SchImportPath
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string UncPath { get; set; } = null!;
    public string? ComponentType { get; set; } // NULL = accept all
    public bool IsActive { get; set; } = true;
}
