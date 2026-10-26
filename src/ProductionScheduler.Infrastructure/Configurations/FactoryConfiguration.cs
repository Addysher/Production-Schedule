using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class FactoryConfiguration : IEntityTypeConfiguration<Factory>
{
    public void Configure(EntityTypeBuilder<Factory> b)
    {
        b.ToTable("Factories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();

        // Deleting a factory leaves its stations in place, just unassigned.
        b.HasMany(x => x.Stations)
            .WithOne(s => s.Factory)
            .HasForeignKey(s => s.FactoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
