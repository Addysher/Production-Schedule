using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> b)
    {
        b.ToTable("Shifts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
    }
}

public class ShiftBreakConfiguration : IEntityTypeConfiguration<ShiftBreak>
{
    public void Configure(EntityTypeBuilder<ShiftBreak> b)
    {
        b.ToTable("ShiftBreaks");
        b.HasKey(x => x.Id);

        b.HasOne(x => x.Shift)
            .WithMany(s => s.Breaks)
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> b)
    {
        b.ToTable("Operators");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();

        b.HasOne(x => x.Shift)
            .WithMany(s => s.Operators)
            .HasForeignKey(x => x.ShiftId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
