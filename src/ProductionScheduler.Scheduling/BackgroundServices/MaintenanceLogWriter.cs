using ProductionScheduler.Domain;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// The background services run unattended, so anything only written to ILogger is invisible to the
// people using the system. Problems and notable events also go to MaintenanceLogs, which the Dashboard's
// "Recent maintenance" panel shows. The caller is responsible for saving.
internal static class MaintenanceLogWriter
{
    public static void Add(AppDbContext db, string taskName, string outcome, string message, string? jobRef = null, string? sourcePath = null)
    {
        db.MaintenanceLogs.Add(new MaintenanceLog
        {
            TaskName = taskName,
            JobRef = jobRef,
            SourcePath = Truncate(sourcePath, 500),
            Outcome = outcome,
            Message = Truncate(message, 500),
            RunAt = SiteClock.Now,
        });
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..(max - 1)] + "…";
}
