using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class BatchcutWatchPathConfiguration : IEntityTypeConfiguration<BatchcutWatchPath>
{
    public void Configure(EntityTypeBuilder<BatchcutWatchPath> b)
    {
        b.ToTable("BatchcutWatchPaths");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.UncPath).HasMaxLength(500).IsRequired();
    }
}

public class BatchcutEventConfiguration : IEntityTypeConfiguration<BatchcutEvent>
{
    public void Configure(EntityTypeBuilder<BatchcutEvent> b)
    {
        b.ToTable("BatchcutEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.FolderName).HasMaxLength(200).IsRequired();
        b.Property(x => x.JobRef).HasMaxLength(50);
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasMaxLength(500);

        b.HasOne(x => x.BatchcutPath)
            .WithMany(p => p.Events)
            .HasForeignKey(x => x.BatchcutPathId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.JobRef, x.Status })
            .HasDatabaseName("IX_BatchcutEvents_JobRef");
    }
}
