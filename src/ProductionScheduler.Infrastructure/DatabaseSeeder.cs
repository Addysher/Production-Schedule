using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure;

// §7 Step 1 / §4.5 / §6.2: default Settings rows and one default Shift. Idempotent — it only adds what
// is missing, so it never overwrites a value an administrator has since changed.
public static class DatabaseSeeder
{
    // Path settings are seeded empty: the doc says "configured at setup", and the background services
    // treat an empty path as "not configured yet" and stay idle. Seeding the keys makes them appear in
    // Admin -> Settings so an administrator can fill them in.
    private static readonly (string Key, string Value)[] DefaultSettings =
    {
        ("AtRiskBufferMinutes", "0"), // "confirmed by operations before go-live"; 0 = unused (At Risk is superseded, §5.0)
        ("ShiftStartHour", "6"),
        ("ShiftEndHour", "18"),
        ("WorkingDays", "1,2,3,4,5"),
        ("ScheduleRetentionMonths", "6"),
        ("PrdPollIntervalMinutes", "5"),
        ("BatchcutPollIntervalSeconds", "30"),
        ("RawFilesPath", ""),
        ("SawFilesDoNotCutPath", ""),
        ("SawFilesForCuttingPath", ""),
        ("SawFilesDoneBasePath", ""),
        ("MmePollIntervalSeconds", "300"),
        ("CreepThresholdPercent", "20"),
        ("MpsPreMoveDaysBeforeProduction", "1"),
        ("NightlyMaintenanceRunHour", "1"), // not named in the doc ("configurable, default 01:00") — see NightlyMaintenanceService
    };

    public static async Task SeedDefaultsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existingKeys = (await db.Settings.Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var now = DateTime.UtcNow;

        foreach (var (key, value) in DefaultSettings.Where(d => !existingKeys.Contains(d.Key)))
        {
            db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = now });
        }

        if (!await db.Shifts.AnyAsync(ct))
        {
            db.Shifts.Add(new Shift { Name = "Day Shift", StartTime = new TimeOnly(6, 0), EndTime = new TimeOnly(18, 0), IsActive = true });
        }

        await db.SaveChangesAsync(ct);
    }
}
