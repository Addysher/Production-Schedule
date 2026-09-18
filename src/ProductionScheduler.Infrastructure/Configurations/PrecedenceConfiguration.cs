using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class PrecedenceConfiguration : IEntityTypeConfiguration<Precedence>
{
    public void Configure(EntityTypeBuilder<Precedence> b)
    {
        b.ToTable("Precedences");
        b.Property(x => x.PredecessorJobRef).HasMaxLength(50);
        b.Property(x => x.SuccessorJobRef).HasMaxLength(50);
        b.HasKey(x => new { x.PredecessorJobRef, x.SuccessorJobRef })
            .HasName("PK_Precedences");

        b.HasOne(x => x.PredecessorJob)
            .WithMany()
            .HasForeignKey(x => x.PredecessorJobRef)
            .HasPrincipalKey(j => j.JobRef)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.SuccessorJob)
            .WithMany()
            .HasForeignKey(x => x.SuccessorJobRef)
            .HasPrincipalKey(j => j.JobRef)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
