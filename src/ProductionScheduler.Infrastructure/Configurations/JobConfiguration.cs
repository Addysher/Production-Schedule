using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> b)
    {
        b.ToTable("Jobs");
        b.HasKey(x => x.Id);

        b.Property(x => x.JobRef).HasMaxLength(50).IsRequired();
        b.HasIndex(x => x.JobRef).IsUnique();

        b.Property(x => x.ComponentType).HasMaxLength(100);
        b.Property(x => x.SchComponentType).HasMaxLength(100);
        b.Property(x => x.ClientName).HasMaxLength(200);
        b.Property(x => x.ClientRefNumber).HasMaxLength(100);
        b.Property(x => x.SiteAddress).HasMaxLength(300);
        b.Property(x => x.SiteSuburb).HasMaxLength(100);
        b.Property(x => x.SupervisorName).HasMaxLength(200);
        b.Property(x => x.SupervisorNumber).HasMaxLength(50);
        b.Property(x => x.DesignStatus).HasMaxLength(20).IsRequired();
        b.Property(x => x.Raw2).HasMaxLength(50);
        b.Property(x => x.Raw7).HasMaxLength(50);
        b.Property(x => x.Raw8).HasMaxLength(50);
        b.Property(x => x.SchSourcePath).HasMaxLength(500);
        b.Property(x => x.MpsSourcePath).HasMaxLength(500);
        b.Property(x => x.InvoiceNumber).HasMaxLength(100);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();

        b.Property(x => x.TotalEqA).HasPrecision(10, 4);
        b.Property(x => x.CompletedEqA).HasPrecision(10, 4);

        b.HasOne(x => x.AssignedStation)
            .WithMany()
            .HasForeignKey(x => x.AssignedStationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ManualStation)
            .WithMany()
            .HasForeignKey(x => x.ManualStationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.MpsStation)
            .WithMany()
            .HasForeignKey(x => x.MpsStationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ApprovedByUser)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.Status, x.DesignStatus, x.IsArchived })
            .HasDatabaseName("IX_Jobs_Status");

        b.HasIndex(x => new { x.AssignedStationId, x.DesignStatus, x.IsArchived })
            .HasDatabaseName("IX_Jobs_Station");

        b.HasIndex(x => new { x.IsManuallyScheduled, x.ManualScheduleDate })
            .HasDatabaseName("IX_Jobs_ManualSchedule");
    }
}
