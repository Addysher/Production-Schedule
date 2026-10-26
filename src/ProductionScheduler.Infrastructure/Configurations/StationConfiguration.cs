using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class StationConfiguration : IEntityTypeConfiguration<Station>
{
    public void Configure(EntityTypeBuilder<Station> b)
    {
        b.ToTable("Stations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.ComponentType).HasMaxLength(100).IsRequired();
        b.Property(x => x.PrdPath).HasMaxLength(500);
        b.Property(x => x.MpsPath).HasMaxLength(500);
        b.Property(x => x.ArchivePath).HasMaxLength(500);
        b.Property(x => x.EqaPerHour).HasPrecision(10, 4);

        b.HasIndex(x => new { x.ComponentType, x.IsActive })
            .HasDatabaseName("IX_Stations_ComponentType");
    }
}

public class StationCapacityOverrideConfiguration : IEntityTypeConfiguration<StationCapacityOverride>
{
    public void Configure(EntityTypeBuilder<StationCapacityOverride> b)
    {
        b.ToTable("StationCapacityOverrides");
        b.HasKey(x => x.Id);
        b.Property(x => x.Note).HasMaxLength(200);

        b.HasOne(x => x.Station)
            .WithMany(s => s.CapacityOverrides)
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.StationId, x.OverrideDate })
            .IsUnique()
            .HasDatabaseName("IX_StationCapacityOverrides_Station_Date");
    }
}
