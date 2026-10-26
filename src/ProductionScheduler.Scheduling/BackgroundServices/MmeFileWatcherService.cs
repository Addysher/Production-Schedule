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
public partial class MmeFileWatcherService : BackgroundService
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
        var knownRefs = await db.Jobs.AsNoTracking().Where(j => !j.IsArchived).Select(j => j.JobRef).ToListAsync(ct);

        RehomeOrphanedFiles(stagingRoot, knownRefs);

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

                var jobRef = ResolveJobRef(Path.GetFileNameWithoutExtension(file), knownRefs);
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
        if (Directory.Exists(stagingRoot))
        {
            foreach (var dir in Directory.GetDirectories(stagingRoot))
            {
                touched.Add(Path.GetFileName(dir));
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

        if (staged.Length > 0 && IsComplete(job, staged))
        {
            if (Directory.Exists(waitingDir))
            {
                foreach (var file in staged)
                {
                    File.Move(file, Path.Combine(waitingDir, Path.GetFileName(file)), overwrite: true);
                }

                Directory.Delete(stagingDir);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(waitingDir)!);
                Directory.Move(stagingDir, waitingDir);
            }

            MaintenanceLogWriter.Add(db, LogTask, "success", $"Saw files for {jobRef} are complete ({staged.Length}) and in the waiting folder.", jobRef, waitingDir);
        }

        var inWaiting = Directory.Exists(waitingDir) ? Directory.GetFiles(waitingDir, "*.mme").Length : 0;
        var stillStaged = Directory.Exists(stagingDir) ? Directory.GetFiles(stagingDir, "*.mme").Length : 0;
        job.SawBundleStatus = inWaiting > 0 && stillStaged == 0 ? "waiting" : stillStaged > 0 ? "bundling" : job.SawBundleStatus;
        job.SawFileCount = inWaiting + stillStaged;
    }

    // Complete = every member the design lists has its file. A job with no members yet (quote, or SCH
    // not imported) is never complete.
    internal static bool IsComplete(Job job, IEnumerable<string> files)
    {
        if (job.Members.Count == 0)
        {
            return false;
        }

        var have = files.Select(f => MemberIdOf(Path.GetFileNameWithoutExtension(f), job.JobRef)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return job.Members.All(m => have.Contains(m.MemberId));
    }

    // Files are named {JobRef}{MemberID}, and member IDs aren't a fixed length ("E17", but also "P05A" in
    // real floor designs). So: the longest known job ref the name starts with; failing that (the SCH file
    // hasn't been imported yet) the job-ref shape — digits then three letters, e.g. 58749MPF; and only
    // then the old "last three characters are the member" rule.
    internal static string ResolveJobRef(string fileNameWithoutExtension, IEnumerable<string> knownRefs)
    {
        var known = knownRefs
            .Where(r => fileNameWithoutExtension.Length > r.Length && fileNameWithoutExtension.StartsWith(r, StringComparison.OrdinalIgnoreCase))
            .MaxBy(r => r.Length);
        if (known is not null)
        {
            return fileNameWithoutExtension[..known.Length];
        }

        var shape = JobRefShape().Match(fileNameWithoutExtension);
        return shape.Success ? shape.Groups[1].Value : DeriveJobRef(fileNameWithoutExtension);
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^(\d+[A-Za-z]{3})(.+)$")]
    private static partial System.Text.RegularExpressions.Regex JobRefShape();

    // "59778MWFE17" -> "59778MWF": the last-resort rule for names that don't fit the job-ref shape.
    internal static string DeriveJobRef(string fileNameWithoutExtension) =>
        fileNameWithoutExtension.Length > 3 ? fileNameWithoutExtension[..^3] : string.Empty;

    internal static string MemberIdOf(string fileNameWithoutExtension, string jobRef) =>
        fileNameWithoutExtension.Length > jobRef.Length ? fileNameWithoutExtension[jobRef.Length..] : string.Empty;

    // A file staged under a guessed job ref (before its SCH arrived) moves to the right job's staging
    // folder once that job is known.
    private void RehomeOrphanedFiles(string stagingRoot, IReadOnlyCollection<string> knownRefs)
    {
        if (!Directory.Exists(stagingRoot))
        {
            return;
        }

        var known = knownRefs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in Directory.GetDirectories(stagingRoot).Where(d => !known.Contains(Path.GetFileName(d))))
        {
            foreach (var file in Directory.GetFiles(dir, "*.mme"))
            {
                var jobRef = ResolveJobRef(Path.GetFileNameWithoutExtension(file), knownRefs);
                if (!known.Contains(jobRef))
                {
                    continue;
                }

                try
                {
                    var dest = Path.Combine(stagingRoot, jobRef);
                    Directory.CreateDirectory(dest);
                    File.Move(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _logger.LogWarning(ex, "Couldn't move {File} to its job's bundle; will retry next pass.", file);
                }
            }

            if (Directory.GetFileSystemEntries(dir).Length == 0)
            {
                Directory.Delete(dir);
            }
        }
    }
}
