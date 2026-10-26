using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.11 — bundles MME cut-list files (one per frame member) into per-job folders awaiting cutting
// approval. Replaces the existing small bundling program entirely.
//
//   RawFilesPath (drop) ──► RawFilesPath\_bundling\{JobRef} ──(every member has its file)──► SawFilesDoNotCutPath\{JobRef}
//                                                                         (Approve for cutting) ──► SawFilesForCuttingPath\{JobRef}
//
// A bundle only reaches the waiting folder once it's complete, so nothing half-exported is ever approved.
public class MmeFileWatcherService : BackgroundService
{
    public const string StagingFolderName = "_bundling";
    internal const string ErrorsFolderName = "_errors";
    private const string LogTask = "MmeBundle";

    // Once the job's bundle is with the saw, new files for it are held back rather than mixed in.
    private static readonly HashSet<string> PastApproval = new(StringComparer.OrdinalIgnoreCase) { "approved", "cut", "in-production", "completed" };

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
        var waitingPath = await SettingsReader.GetStringAsync(db, "SawFilesDoNotCutPath", "", ct);
        var settleSeconds = await SettingsReader.GetIntAsync(db, "SchFileSettleSeconds", 5, ct);

        if (string.IsNullOrWhiteSpace(rawFilesPath) || string.IsNullOrWhiteSpace(waitingPath) || !Directory.Exists(rawFilesPath))
        {
            return;
        }

        var stagingRoot = Path.Combine(rawFilesPath, StagingFolderName);
        var touched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Collect newly dropped files. Each file is handled on its own, so one locked or badly named
        // file never holds up the rest.
        foreach (var file in Directory.GetFiles(rawFilesPath, "*.mme"))
        {
            try
            {
                if (!SchImportService.IsSettled(file, settleSeconds))
                {
                    continue; // still being written by the design program
                }

                var jobRef = DeriveJobRef(Path.GetFileNameWithoutExtension(file));
                if (string.IsNullOrEmpty(jobRef))
                {
                    var errorsDir = Path.Combine(rawFilesPath, ErrorsFolderName);
                    Directory.CreateDirectory(errorsDir);
                    File.Move(file, Path.Combine(errorsDir, Path.GetFileName(file)), overwrite: true);
                    MaintenanceLogWriter.Add(db, LogTask, "error", $"'{Path.GetFileName(file)}' isn't named {{JobRef}}{{MemberID}}.mme; moved to {ErrorsFolderName}.", sourcePath: file);
                    continue;
                }

                var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.JobRef == jobRef, ct);
                var waitingDir = Path.Combine(waitingPath, jobRef);

                // A late or revised file for a bundle that's already complete goes straight to it (the design
                // program is the source of truth, so a newer file replaces the older one). Once the job is
                // approved, new files are held in staging for the office to deal with.
                var heldBack = job is not null && PastApproval.Contains(job.Status);
                var destDir = !heldBack && Directory.Exists(waitingDir) ? waitingDir : Path.Combine(stagingRoot, jobRef);
                Directory.CreateDirectory(destDir);
                File.Move(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
                touched.Add(jobRef);

                if (heldBack)
                {
                    MaintenanceLogWriter.Add(db, LogTask, "error",
                        $"New saw file '{Path.GetFileName(file)}' arrived after {jobRef} was approved for cutting. It's held in {StagingFolderName}\\{jobRef}; check whether the saw has the latest design.",
                        jobRef, file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't bundle {File}; will retry next pass.", file);
            }
        }

        // 2. Promote every complete staged bundle (including ones finished by an SCH import since the last
        // pass) and refresh what each touched job shows.
        // Waiting folders are included too, so bundles that were already there before jobs tracked their
        // bundle state (or that someone changed by hand) show their real state.
        foreach (var root in new[] { stagingRoot, waitingPath })
        {
            if (Directory.Exists(root))
            {
                foreach (var dir in Directory.GetDirectories(root))
                {
                    touched.Add(Path.GetFileName(dir));
                }
            }
        }

        foreach (var jobRef in touched)
        {
            try
            {
                await ReconcileAsync(db, jobRef, Path.Combine(stagingRoot, jobRef), Path.Combine(waitingPath, jobRef), ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't move the saw-file bundle for {JobRef}; will retry next pass.", jobRef);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ReconcileAsync(AppDbContext db, string jobRef, string stagingDir, string waitingDir, CancellationToken ct)
    {
        var job = await db.Jobs.Include(j => j.Members).FirstOrDefaultAsync(j => j.JobRef == jobRef, ct);
        var staged = Directory.Exists(stagingDir) ? Directory.GetFiles(stagingDir, "*.mme") : [];

        if (job is null)
        {
            // Normal when the saw files beat the SCH file; the bundle waits in staging for it.
            _logger.LogInformation("Saw files for {JobRef} are waiting for its SCH import.", jobRef);
            return;
        }

        if (PastApproval.Contains(job.Status))
        {
            return; // held files were logged when they arrived
        }

        // Completeness counts every file the job has, wherever it is, so a waiting folder that only holds
        // part of the design (left by the old bundler, or edited by hand) isn't reported as ready.
        var waiting = Directory.Exists(waitingDir) ? Directory.GetFiles(waitingDir, "*.mme") : [];
        var complete = IsComplete(job, staged.Concat(waiting));

        if (staged.Length > 0 && complete)
        {
            // File by file rather than Directory.Move: the drop and waiting folders may be on different
            // drives or shares, where a directory move fails.
            Directory.CreateDirectory(waitingDir);
            foreach (var file in staged)
            {
                File.Move(file, Path.Combine(waitingDir, Path.GetFileName(file)), overwrite: true);
            }

            if (Directory.GetFileSystemEntries(stagingDir).Length == 0)
            {
                Directory.Delete(stagingDir);
            }

            MaintenanceLogWriter.Add(db, LogTask, "success", $"Saw files for {jobRef} are complete ({staged.Length + waiting.Length}) and in the waiting folder.", jobRef, waitingDir);
        }

        var inWaiting = Directory.Exists(waitingDir) ? Directory.GetFiles(waitingDir, "*.mme").Length : 0;
        var stillStaged = Directory.Exists(stagingDir) ? Directory.GetFiles(stagingDir, "*.mme").Length : 0;
        job.SawBundleStatus = inWaiting + stillStaged == 0 ? job.SawBundleStatus : complete && stillStaged == 0 ? "waiting" : "bundling";
        job.SawFileCount = inWaiting + stillStaged;
    }

    // Complete = every member the design lists has its file. A job with no members yet (quote, or SCH
    // not imported) is never complete.
    public static bool IsComplete(Job job, IEnumerable<string> files)
    {
        if (job.Members.Count == 0)
        {
            return false;
        }

        // Sapphire always ends a saw file's name with a 3-character member ID, so 3-character members
        // must each have their file. A member whose SCH ID is another length (e.g. "P05A" in real floor
        // designs) can't be matched by name, so those are only counted: there must be at least one
        // further file for each of them.
        var have = files.Select(f => DeriveMemberId(Path.GetFileNameWithoutExtension(f))).ToList();
        var haveSet = have.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var named = job.Members.Where(m => m.MemberId.Length == 3).ToList();
        var unnamed = job.Members.Count - named.Count;
        var spare = have.Count(id => !named.Any(m => string.Equals(m.MemberId, id, StringComparison.OrdinalIgnoreCase)));
        return named.All(m => haveSet.Contains(m.MemberId)) && spare >= unnamed;
    }

    // Sapphire's rule: a saw file is named {JobRef}{MemberID}.mme with a 3-character member ID, so the
    // job is the name minus its last 3 characters ("59778MWFE17" -> "59778MWF").
    internal static string DeriveJobRef(string fileNameWithoutExtension) =>
        fileNameWithoutExtension.Length > 3 ? fileNameWithoutExtension[..^3] : string.Empty;

    internal static string DeriveMemberId(string fileNameWithoutExtension) =>
        fileNameWithoutExtension.Length > 3 ? fileNameWithoutExtension[^3..] : string.Empty;
}
