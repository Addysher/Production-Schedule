using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Configurations;

public class PrdReadStateConfiguration : IEntityTypeConfiguration<PrdReadState>
{
    public void Configure(EntityTypeBuilder<PrdReadState> b)
    {
        b.ToTable("PrdReadState");
        b.HasKey(x => new { x.StationId, x.FileDate })
            .HasName("PK_PrdReadState");

        b.HasOne(x => x.Station)
            .WithMany()
            .HasForeignKey(x => x.StationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.StationId, x.FileDate })
            .HasDatabaseName("IX_PrdReadState_Station_Date");
    }
}
