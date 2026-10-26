using ProductionScheduler.Domain;
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
        // Activated for the Dashboard's "Jobs at risk" tile: a job is at risk once its scheduled
        // completion is within this many minutes of its booked delivery date (and not already late).
        // Default 1440 = 24 hours.
        ("AtRiskBufferMinutes", "1440"),
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
        ("SchFileSettleSeconds", "5"), // ignore an .sch file until it has been unchanged this long (Sapphire may still be writing it)
        ("NightlyMaintenanceRunHour", "1"), // not named in the doc ("configurable, default 01:00") — see NightlyMaintenanceService
        ("FreezeWindowDays", "3"), // v3.0 lock-out: today + this many working days are frozen; 0 = off (see FreezeWindow)
        ("ProductionLeadDaysMin", "1"), // v3.0: a job must be finished at least this many working days before its booked delivery
        ("ProductionLeadDaysMax", "5"), // ...and is not started more than this many working days before it (yard space); 0 = no limit
        ("PencilTbaJobs", "true"), // v3.0: plan jobs with no booked delivery date (TBA, incl. quotes) as pencilled-in work

        // v3.0 email reports (§5.21, EmailReportService). A report with no recipient isn't sent.
        ("SmtpHost", ""),
        ("SmtpPort", "25"),
        ("SmtpUseSsl", "false"),
        ("SmtpUsername", ""),
        ("SmtpPassword", ""), // never sent back to the browser (SettingsController masks it)
        ("SmtpFromAddress", ""),
        ("EmailPickupDirectory", ""), // set to write .eml files to a folder instead of sending (testing)
        ("EmailNextDayJobsTo", ""),
        ("EmailNextDayJobsHour", "15"),
        ("EmailReadyForProductionTo", ""),
        ("EmailReadyForProductionHour", "7"),
        ("EmailReadyForProductionDays", "10"),
        ("EmailDailyProductionTo", ""),
        ("EmailDailyProductionHour", "17"),

        // Appearance — read by any authenticated user (SettingsController) and applied client-side
        // (client/src/theme/applyTheme.ts) so Admin and Viewer see the same branding/mode.
        ("ThemePrimaryColor", "#0a5e73"),
        ("ThemeSecondaryColor", "#fdb913"),
        ("ThemeMode", "system"), // 'system' | 'light' | 'dark'
    };

    public static async Task SeedDefaultsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existingKeys = (await db.Settings.Select(s => s.Key).ToListAsync(ct)).ToHashSet();
        var now = SiteClock.Now;

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
