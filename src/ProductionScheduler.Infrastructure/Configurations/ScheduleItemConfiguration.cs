using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class ScheduleItemConfiguration : IEntityTypeConfiguration<ScheduleItem>
{
    public void Configure(EntityTypeBuilder<ScheduleItem> b)
    {
        b.ToTable("ScheduleItems");
        b.HasKey(x => x.Id);

        b.Property(x => x.PlannedEqA).HasPrecision(10, 4);
        b.Property(x => x.EqaCompleted).HasPrecision(10, 4);

        b.HasOne(x => x.Job)
            .WithMany(j => j.ScheduleItems)
            .HasForeignKey(x => x.JobRef)
            .HasPrincipalKey(j => j.JobRef)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Operator)
            .WithMany(o => o.ScheduleItems)
            .HasForeignKey(x => x.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.CreepApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.CreepApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.JobRef, x.ScheduleDate })
            .IsUnique()
            .HasDatabaseName("UQ_ScheduleItems_Job_Date");

        b.HasIndex(x => new { x.ScheduleDate, x.StationId })
            .HasDatabaseName("IX_ScheduleItems_Date_Station");

        b.HasIndex(x => new { x.CreepDetected, x.CreepApprovedAt })
            .HasDatabaseName("IX_ScheduleItems_Creep");
    }
}
