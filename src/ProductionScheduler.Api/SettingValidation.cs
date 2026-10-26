using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api;

// Settings are free-form key/value rows, but most are read as numbers by the scheduler and the
// background services — a bad value there can hang scheduling or stop a service. Known keys are
// checked here before saving; unknown keys and folder paths (which may be blank = "not set up") pass.
public static partial class SettingValidation
{
    private static readonly Dictionary<string, (int Min, int Max)> IntRanges = new()
    {
        ["ShiftStartHour"] = (0, 23),
        ["ShiftEndHour"] = (1, 24),
        ["NightlyMaintenanceRunHour"] = (0, 23),
        ["PrdPollIntervalMinutes"] = (1, 1440),
        ["BatchcutPollIntervalSeconds"] = (5, 86400),
        ["MmePollIntervalSeconds"] = (5, 86400),
        ["SchFileSettleSeconds"] = (0, 3600),
        ["MpsPreMoveDaysBeforeProduction"] = (0, 14),
        ["CreepThresholdPercent"] = (1, 100),
        ["ScheduleRetentionMonths"] = (1, 120),
        ["AtRiskBufferMinutes"] = (0, 100_000),
        ["FreezeWindowDays"] = (0, 30),
        ["EmailNextDayJobsHour"] = (0, 23),
        ["EmailReadyForProductionHour"] = (0, 23),
        ["EmailDailyProductionHour"] = (0, 23),
        ["EmailReadyForProductionDays"] = (1, 60),
        ["SmtpPort"] = (1, 65535),
    };

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColour();

    public static async Task<string?> ProblemAsync(AppDbContext db, string key, string value)
    {
        if (IntRanges.TryGetValue(key, out var range))
        {
            if (!int.TryParse(value, out var n) || n < range.Min || n > range.Max)
            {
                return $"{key} must be a whole number from {range.Min} to {range.Max}.";
            }

            if (key is "ShiftStartHour" or "ShiftEndHour")
            {
                var other = key == "ShiftStartHour" ? "ShiftEndHour" : "ShiftStartHour";
                var otherValue = await db.Settings.Where(s => s.Key == other).Select(s => s.Value).FirstOrDefaultAsync();
                if (int.TryParse(otherValue, out var o))
                {
                    var (start, end) = key == "ShiftStartHour" ? (n, o) : (o, n);
                    if (start >= end)
                    {
                        return $"The shift must start before it ends (start {start}:00, end {end}:00).";
                    }
                }
            }

            return null;
        }

        return key switch
        {
            "WorkingDays" => WorkingDaysProblem(value),
            "ThemeMode" when value is not ("system" or "light" or "dark") => "ThemeMode must be system, light or dark.",
            "ThemePrimaryColor" or "ThemeSecondaryColor" when !HexColour().IsMatch(value) => $"{key} must be a colour like #0a5e73.",
            _ => null,
        };
    }

    private static string? WorkingDaysProblem(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => !int.TryParse(p, out var d) || d < 0 || d > 6))
        {
            return "WorkingDays must list at least one day as numbers 0-6 (0 = Sunday), e.g. 1,2,3,4,5.";
        }

        return null;
    }
}
