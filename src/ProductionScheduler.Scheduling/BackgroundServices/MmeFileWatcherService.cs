using ProductionScheduler.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.11 — bundles MME cut-list files (one per frame member) into per-job folders awaiting
// cutting approval. Replaces the existing small bundling program entirely.
public class MmeFileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MmeFileWatcherService> _logger;

    public MmeFileWatcherService(IServiceScopeFactory scopeFactory, ILogger<MmeFileWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MmeFileWatcherService poll failed.");
            }

            var intervalSeconds = await SettingsReader.GetLoopSettingAsync(_scopeFactory, "MmePollIntervalSeconds", 300, 5, 86400, stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var rawFilesPath = await SettingsReader.GetStringAsync(db, "RawFilesPath", "", ct);
        var doNotCutPath = await SettingsReader.GetStringAsync(db, "SawFilesDoNotCutPath", "", ct);

        if (string.IsNullOrWhiteSpace(rawFilesPath) || string.IsNullOrWhiteSpace(doNotCutPath) || !Directory.Exists(rawFilesPath))
        {
            return;
        }

        var mmeFiles = Directory.GetFiles(rawFilesPath, "*.mme");
        if (mmeFiles.Length == 0)
        {
            return;
        }

        var groups = mmeFiles.GroupBy(f => DeriveJobRef(Path.GetFileNameWithoutExtension(f)));

        foreach (var group in groups)
        {
            var jobRef = group.Key;
            if (string.IsNullOrEmpty(jobRef))
            {
                continue;
            }

            var destDir = Path.Combine(doNotCutPath, jobRef);
            Directory.CreateDirectory(destDir); // no-op if it already exists (partial bundle from a prior pass)

            foreach (var file in group)
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                if (!File.Exists(destFile))
                {
                    File.Move(file, destFile);
                }
            }

            var jobExists = await db.Jobs.AnyAsync(j => j.JobRef == jobRef, ct);
            if (jobExists)
            {
                _logger.LogInformation("Bundled MME files for job {JobRef} into {DestDir}.", jobRef, destDir);
            }
            else
            {
                _logger.LogWarning("Bundled MME files for job {JobRef} but no matching job exists yet (SCH import pending?).", jobRef);
            }
        }
    }

    // "59778MWFE17" -> "59778MWF" (member IDs are always 3 characters, e.g. E17, I31).
    internal static string DeriveJobRef(string fileNameWithoutExtension) =>
        fileNameWithoutExtension.Length > 3 ? fileNameWithoutExtension[..^3] : string.Empty;
}
