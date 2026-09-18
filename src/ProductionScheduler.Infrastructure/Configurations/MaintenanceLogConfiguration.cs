using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class MaintenanceLogConfiguration : IEntityTypeConfiguration<MaintenanceLog>
{
    public void Configure(EntityTypeBuilder<MaintenanceLog> b)
    {
        b.ToTable("MaintenanceLogs");
        b.HasKey(x => x.Id);

        b.Property(x => x.TaskName).HasMaxLength(100).IsRequired();
        b.Property(x => x.JobRef).HasMaxLength(50);
        b.Property(x => x.SourcePath).HasMaxLength(500);
        b.Property(x => x.DestPath).HasMaxLength(500);
        b.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        b.Property(x => x.Message).HasMaxLength(500);
    }
}
