using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    // For a background service's own loop timing: never throws and is always within [min, max], so a
    // database blip or an out-of-range value can't stop the service (which by default stops the whole API).
    public static async Task<int> GetLoopSettingAsync(
        IServiceScopeFactory scopeFactory, string key, int defaultValue, int min, int max, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            return Math.Clamp(await GetIntAsync(db, key, defaultValue, ct), min, max);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return defaultValue;
        }
    }
}
