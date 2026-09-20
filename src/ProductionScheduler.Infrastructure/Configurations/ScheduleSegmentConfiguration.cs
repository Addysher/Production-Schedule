using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class ScheduleSegmentConfiguration : IEntityTypeConfiguration<ScheduleSegment>
{
    public void Configure(EntityTypeBuilder<ScheduleSegment> b)
    {
        b.ToTable("ScheduleSegments");
        b.HasKey(x => x.Id);
        b.Property(x => x.PlannedEqA).HasPrecision(10, 4);

        b.HasOne(x => x.ScheduleItem)
            .WithMany(i => i.Segments)
            .HasForeignKey(x => x.ScheduleItemId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.SegmentDate).HasDatabaseName("IX_ScheduleSegments_Date");
    }
}
