using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<StationCapacityOverride> StationCapacityOverrides => Set<StationCapacityOverride>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ShiftBreak> ShiftBreaks => Set<ShiftBreak>();
    public DbSet<Operator> Operators => Set<Operator>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobMember> JobMembers => Set<JobMember>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    public DbSet<SetupTime> SetupTimes => Set<SetupTime>();
    public DbSet<Precedence> Precedences => Set<Precedence>();
    public DbSet<ScheduleItem> ScheduleItems => Set<ScheduleItem>();
    public DbSet<SchImportPath> SchImportPaths => Set<SchImportPath>();
    public DbSet<PrdReadState> PrdReadStates => Set<PrdReadState>();
    public DbSet<BatchcutWatchPath> BatchcutWatchPaths => Set<BatchcutWatchPath>();
    public DbSet<BatchcutEvent> BatchcutEvents => Set<BatchcutEvent>();
    public DbSet<JobType> JobTypes => Set<JobType>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
