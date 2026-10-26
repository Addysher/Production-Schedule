namespace ProductionScheduler.Domain.Entities;

public class Station
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string ComponentType { get; set; } = null!;
    public string? UncPath { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }

    // Referenced in §7 (AdminController CRUD, dashboard cards) but not listed in the §4.2 table body.
    public decimal? DailyEqATarget { get; set; }
    public string? ArchivePath { get; set; } // used by Nightly Task 4 (MPS archive)

    public int? FactoryId { get; set; } // v3.0: which factory's kiosk shows it; NULL = not assigned
    public Factory? Factory { get; set; }

    public ICollection<StationCapacityOverride> CapacityOverrides { get; set; } = new List<StationCapacityOverride>();
}
