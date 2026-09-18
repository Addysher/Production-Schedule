using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class JobMemberConfiguration : IEntityTypeConfiguration<JobMember>
{
    public void Configure(EntityTypeBuilder<JobMember> b)
    {
        b.ToTable("JobMembers");
        b.HasKey(x => x.Id);

        b.Property(x => x.MemberId).HasMaxLength(20).IsRequired();
        b.Property(x => x.ComponentType).HasMaxLength(100);
        b.Property(x => x.MemberCategory).HasMaxLength(100);
        b.Property(x => x.Raw2).HasMaxLength(50);

        b.Property(x => x.LinearLengthM).HasPrecision(8, 3);
        b.Property(x => x.EqaValue).HasPrecision(10, 4).IsRequired();
        b.Property(x => x.CompletedEqA).HasPrecision(10, 4);

        b.HasOne(x => x.Job)
            .WithMany(j => j.Members)
            .HasForeignKey(x => x.JobRef)
            .HasPrincipalKey(j => j.JobRef)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => new { x.JobRef, x.MemberId })
            .IsUnique()
            .HasDatabaseName("UQ_JobMembers_Ref");

        b.HasIndex(x => x.JobRef)
            .HasDatabaseName("IX_JobMembers_JobRef");
    }
}
