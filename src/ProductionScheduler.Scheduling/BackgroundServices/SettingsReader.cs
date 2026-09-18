using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// Small shared helper — background services re-read their poll interval and configured paths
// from the Settings table on every cycle, so admin changes take effect on the next tick.
public static class SettingsReader
{
    public static async Task<string> GetStringAsync(AppDbContext db, string key, string defaultValue, CancellationToken ct = default)
    {
        var setting = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        return setting?.Value ?? defaultValue;
    }

    public static async Task<int> GetIntAsync(AppDbContext db, string key, int defaultValue, CancellationToken ct = default)
    {
        var value = await GetStringAsync(db, key, defaultValue.ToString(), ct);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
