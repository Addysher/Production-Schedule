using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Username).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Username).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        b.Property(x => x.Role).HasMaxLength(20).IsRequired();
        b.HasOne(x => x.Factory).WithMany().HasForeignKey(x => x.FactoryId).OnDelete(DeleteBehavior.SetNull);
    }
}
