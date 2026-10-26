using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class PlanBaselineSegmentConfiguration : IEntityTypeConfiguration<PlanBaselineSegment>
{
    public void Configure(EntityTypeBuilder<PlanBaselineSegment> b)
    {
        b.ToTable("PlanBaselineSegments");
        b.HasKey(x => x.Id);
        b.Property(x => x.JobRef).HasMaxLength(50).IsRequired();
        b.Property(x => x.PlannedEqA).HasPrecision(10, 4);
        b.HasIndex(x => new { x.Day, x.StationId }).HasDatabaseName("IX_PlanBaselineSegments_Day_Station");
    }
}
