namespace ProductionScheduler.Domain.Entities;

// What the factory makes (e.g. "Roof Trusses"). A job's product is decided by the pool folder its
// SCH file was dropped into, not by the SCH file's own component type. Name is the key that
// Stations.ComponentType, JobTypes.ComponentType and Jobs.ComponentType hold.
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? PoolPath { get; set; }    // where the design program drops this product's SCH + MPS files; NULL = not set up yet
    public string? ArchivePath { get; set; } // imported SCH files (and MPS files of jobs deleted before reaching a station)
    public bool IsActive { get; set; } = true;
}
