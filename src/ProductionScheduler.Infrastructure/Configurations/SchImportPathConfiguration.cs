using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class SchImportPathConfiguration : IEntityTypeConfiguration<SchImportPath>
{
    public void Configure(EntityTypeBuilder<SchImportPath> b)
    {
        b.ToTable("SchImportPaths");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.UncPath).HasMaxLength(500).IsRequired();
        b.Property(x => x.ComponentType).HasMaxLength(100);
    }
}
