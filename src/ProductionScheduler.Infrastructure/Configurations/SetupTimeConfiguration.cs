using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class SetupTimeConfiguration : IEntityTypeConfiguration<SetupTime>
{
    public void Configure(EntityTypeBuilder<SetupTime> b)
    {
        b.ToTable("SetupTimes");
        b.Property(x => x.FromComponentType).HasMaxLength(100);
        b.Property(x => x.ToComponentType).HasMaxLength(100);
        b.HasKey(x => new { x.FromComponentType, x.ToComponentType })
            .HasName("PK_SetupTimes");
    }
}
