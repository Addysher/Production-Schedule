using ProductionScheduler.Infrastructure;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.8 — watches one or more configured SchImportPaths for new .sch files (semicolon-delimited,
// dd/mm/yyyy dates) and runs the two-stage quote / finaldesign import workflow.
//
// The doc never says a processed .sch file is moved or deleted immediately (deletion only happens
// via Nightly Task 3, on job completion — §5.13) — so left in place, the same file would be
// reprocessed on every poll forever. To make polling idempotent, this service moves each file it
// handles into a "_processed" or "_errors" subfolder of its import path once done, and records the
// final path as Jobs.SchSourcePath so Task 3 still deletes the right file later.
public class SchImportService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SchImportService> _logger;

    public SchImportService(IServiceScopeFactory scopeFactory, ILogger<SchImportService> logger)
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
                _logger.LogError(ex, "SchImportService poll failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); // no dedicated interval setting is defined for this service
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var importPaths = await db.SchImportPaths.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var anyImported = false;

        foreach (var importPath in importPaths)
        {
            if (!Directory.Exists(importPath.UncPath))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(importPath.UncPath, "*.sch"))
            {
                if (await ProcessFileAsync(db, importPath, file, ct))
                {
                    anyImported = true;
                }
            }
        }

        // Newly-imported/updated 'ready' jobs need a station/slot — run the scheduler once per
        // poll cycle (not per file) so a batch of dropped files only triggers a single re-run.
        if (anyImported)
        {
            var scheduleGenerationService = scope.ServiceProvider.GetRequiredService<IScheduleGenerationService>();
            await scheduleGenerationService.GenerateAsync(ct);
        }
    }

    private async Task<bool> ProcessFileAsync(AppDbContext db, SchImportPath importPath, string filePath, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var parsed = ParseFile(lines);

        if (parsed.ScheduleJob is null)
        {
            _logger.LogError("SCH file {File} is malformed — no valid ScheduleJob record found.", filePath);
            MoveTo(filePath, importPath.UncPath, "_errors");
            return false;
        }

        var job = parsed.ScheduleJob;

        if (importPath.ComponentType is not null && !string.Equals(importPath.ComponentType, job.ComponentType, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "SCH file {File} has ComponentType '{Actual}' but import path '{PathName}' only accepts '{Expected}'.",
                filePath, job.ComponentType, importPath.Name, importPath.ComponentType);
            MoveTo(filePath, importPath.UncPath, "_errors");
            return false;
        }

        var existing = await db.Jobs.FirstOrDefaultAsync(j => j.JobRef == job.JobRef, ct);
        var isFinalDesign = string.Equals(job.DesignStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase);

        if (!isFinalDesign)
        {
            if (existing is not null)
            {
                _logger.LogError("SCH file {File}: job '{JobRef}' already exists — quote already imported.", filePath, job.JobRef);
                MoveTo(filePath, importPath.UncPath, "_errors");
                return false;
            }

            await CreateJobAsync(db, job, parsed.ScheduleEntries, filePath, ct);
        }
        else if (existing is null)
        {
            // finaldesign arriving with no prior quote — treated as a brand new job, all fields set.
            await CreateJobAsync(db, job, parsed.ScheduleEntries, filePath, ct);
        }
        else
        {
            await UpdateJobAsync(db, existing, job, parsed.ScheduleEntries, filePath, ct);
        }

        await db.SaveChangesAsync(ct);
        var processedPath = MoveTo(filePath, importPath.UncPath, "_processed");

        // Nightly Task 3 deletes whatever SchSourcePath names, so it must be the file's final
        // location — not the inbox path it has just left.
        await db.Jobs.Where(j => j.JobRef == job.JobRef)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.SchSourcePath, processedPath), ct);

        _logger.LogInformation("Imported SCH file {File} for job {JobRef} ({Stage}).", filePath, job.JobRef, job.DesignStatus);
        return true;
    }

    // MPS files are never described as arriving anywhere by the design doc — §5.13 Task 1 assumes
    // Jobs.MpsSourcePath is already set, but nothing populates it. Sapphire drops MME files into
    // RawFilesPath (§5.11), and MPS files are the same per-job MiTek export family, so this treats
    // RawFilesPath as their source location too — the same inferred base Task 1's own template
    // ("{MpsSourcePath}\{JobRef}__.MPS") expects.
    private static async Task<string?> GetMpsSourcePathAsync(AppDbContext db, CancellationToken ct) =>
        await SettingsReader.GetStringAsync(db, "RawFilesPath", "", ct) is { Length: > 0 } path ? path : null;

    private static async Task CreateJobAsync(
        AppDbContext db, ParsedScheduleJob job, List<ParsedScheduleEntry> entries, string filePath, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var entity = new Job
        {
            JobRef = job.JobRef,
            ComponentType = job.ComponentType,
            ClientName = job.ClientName,
            ExpectedDeliveryDate = job.ExpectedDeliveryDate,
            EstBookedDeliveryDate = job.EstBookedDeliveryDate,
            Raw2 = job.Raw2,
            Raw7 = job.Raw7,
            Raw8 = job.Raw8,
            ClientRefNumber = job.ClientRefNumber,
            SiteAddress = job.SiteAddress,
            SiteSuburb = job.SiteSuburb,
            JobOrderDate = job.JobOrderDate,
            JobOrderTime = job.JobOrderTime,
            DesignStatus = job.DesignStatus,
            Status = string.Equals(job.DesignStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase) ? "ready" : "scheduled",
            SchSourcePath = filePath,
            MpsSourcePath = await GetMpsSourcePathAsync(db, ct),
            TotalEqA = entries.Sum(e => e.EqaValue),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Jobs.Add(entity);

        foreach (var entry in entries)
        {
            db.JobMembers.Add(ToJobMemberEntity(entry));
        }

        await Task.CompletedTask;
        _ = ct;
    }

    private static async Task UpdateJobAsync(
        AppDbContext db, Job existing, ParsedScheduleJob job, List<ParsedScheduleEntry> entries, string filePath, CancellationToken ct)
    {
        // JobOrderDate/Time and manually-entered fields (SupervisorName/Number, BookedDeliveryDate,
        // InvoiceNumber, Delivered) are never touched by a re-import (§4.4).
        existing.ComponentType = job.ComponentType;
        existing.ClientName = job.ClientName;
        existing.ExpectedDeliveryDate = job.ExpectedDeliveryDate;
        existing.EstBookedDeliveryDate = job.EstBookedDeliveryDate;
        existing.Raw2 = job.Raw2;
        existing.Raw7 = job.Raw7;
        existing.Raw8 = job.Raw8;
        existing.ClientRefNumber = job.ClientRefNumber;
        existing.SiteAddress = job.SiteAddress;
        existing.SiteSuburb = job.SiteSuburb;
        existing.DesignStatus = job.DesignStatus;
        existing.Status = "ready";
        existing.SchSourcePath = filePath;
        existing.MpsSourcePath ??= await GetMpsSourcePathAsync(db, ct);
        existing.TotalEqA = entries.Sum(e => e.EqaValue);
        existing.UpdatedAt = DateTime.UtcNow;

        var existingMembers = await db.JobMembers.Where(m => m.JobRef == job.JobRef).ToListAsync(ct);
        foreach (var entry in entries)
        {
            var member = existingMembers.FirstOrDefault(m => m.MemberId == entry.MemberId);
            if (member is null)
            {
                db.JobMembers.Add(ToJobMemberEntity(entry));
            }
            else
            {
                // Descriptive/dimensional fields refresh from the new design; production-tracking
                // fields (Done/CompletedAt/CompletedEqA) are owned by the PRD reader and left alone.
                member.ComponentType = entry.ComponentType;
                member.MemberCategory = entry.MemberCategory;
                member.LinearLengthM = entry.LinearLengthM;
                member.WidthMm = entry.WidthMm;
                member.HeightMm = entry.HeightMm;
                member.EqaValue = entry.EqaValue;
                member.Batch = entry.Batch;
                member.Sequence = entry.Sequence;
                member.SubSequence = entry.SubSequence;
                member.Raw2 = entry.Raw2;
            }
        }
    }

    private static JobMember ToJobMemberEntity(ParsedScheduleEntry entry) => new()
    {
        JobRef = entry.JobRef,
        MemberId = entry.MemberId,
        ComponentType = entry.ComponentType,
        MemberCategory = entry.MemberCategory,
        LinearLengthM = entry.LinearLengthM,
        WidthMm = entry.WidthMm,
        HeightMm = entry.HeightMm,
        Batch = entry.Batch,
        Sequence = entry.Sequence,
        SubSequence = entry.SubSequence,
        EqaValue = entry.EqaValue,
        Done = entry.Done,
        Raw2 = entry.Raw2
    };

    private static string MoveTo(string filePath, string importRoot, string subfolder)
    {
        var destDir = Path.Combine(importRoot, subfolder);
        Directory.CreateDirectory(destDir);
        var destFile = Path.Combine(destDir, Path.GetFileName(filePath));
        if (File.Exists(destFile))
        {
            destFile = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(filePath)}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{Path.GetExtension(filePath)}");
        }
        File.Move(filePath, destFile);
        return destFile;
    }

    internal record ParsedFile(ParsedScheduleJob? ScheduleJob, List<ParsedScheduleEntry> ScheduleEntries);

    internal record ParsedScheduleJob(
        string JobRef, string? Raw2, string ComponentType, string? ClientName,
        DateOnly? ExpectedDeliveryDate, DateOnly? EstBookedDeliveryDate, string? Raw7, string? Raw8,
        string? ClientRefNumber, string? SiteAddress, string? SiteSuburb,
        DateOnly? JobOrderDate, TimeOnly? JobOrderTime, string DesignStatus);

    internal record ParsedScheduleEntry(
        string JobRef, string? Raw2, string? ComponentType, string MemberId, string? MemberCategory,
        decimal? LinearLengthM, int? WidthMm, int? HeightMm, bool Done, decimal EqaValue,
        int Batch, int Sequence, int SubSequence);

    // A ScheduleJob line has exactly 16 positions; a ScheduleEntry line has 27 or more (several
    // positions between the documented ones are unlabelled MiTek fields we don't persist).
    internal static ParsedFile ParseFile(IEnumerable<string> lines)
    {
        ParsedScheduleJob? scheduleJob = null;
        var entries = new List<ParsedScheduleEntry>();

        foreach (var rawLine in lines)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var fields = rawLine.Split(';');

            if (fields.Length == 16 && scheduleJob is null)
            {
                scheduleJob = TryParseScheduleJob(fields);
            }
            else if (fields.Length >= 27)
            {
                var entry = TryParseScheduleEntry(fields);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
        }

        return new ParsedFile(scheduleJob, entries);
    }

    private static ParsedScheduleJob? TryParseScheduleJob(string[] f)
    {
        if (!TryParseDate(f[4], out var expectedDelivery) && !string.IsNullOrWhiteSpace(f[4]))
        {
            return null;
        }
        if (!TryParseDate(f[5], out var estBookedDelivery) && !string.IsNullOrWhiteSpace(f[5]))
        {
            return null;
        }
        if (!TryParseDate(f[13], out var jobOrderDate) && !string.IsNullOrWhiteSpace(f[13]))
        {
            return null;
        }

        TimeOnly? jobOrderTime = TimeOnly.TryParseExact(f[14], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;

        var designStatus = f[15].Trim();
        if (!string.Equals(designStatus, "Quote", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(designStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        designStatus = string.Equals(designStatus, "Quote", StringComparison.OrdinalIgnoreCase) ? "Quote" : "FinalDesign";

        return new ParsedScheduleJob(
            JobRef: f[0],
            Raw2: NullIfEmpty(f[1]),
            ComponentType: f[2],
            ClientName: NullIfEmpty(f[3]),
            ExpectedDeliveryDate: expectedDelivery,
            EstBookedDeliveryDate: estBookedDelivery,
            Raw7: NullIfEmpty(f[6]),
            Raw8: NullIfEmpty(f[7]),
            ClientRefNumber: NullIfEmpty(f[8]),
            SiteAddress: NullIfEmpty(f[9]),
            SiteSuburb: NullIfEmpty(f[10]),
            JobOrderDate: jobOrderDate,
            JobOrderTime: jobOrderTime,
            DesignStatus: designStatus);
    }

    private static ParsedScheduleEntry? TryParseScheduleEntry(string[] f)
    {
        if (string.IsNullOrWhiteSpace(f[0]) || string.IsNullOrWhiteSpace(f[3]))
        {
            return null;
        }

        if (!decimal.TryParse(f[22], NumberStyles.Number, CultureInfo.InvariantCulture, out var eqaValue))
        {
            return null;
        }

        if (!int.TryParse(f[24], out var batch) || !int.TryParse(f[25], out var sequence) || !int.TryParse(f[26], out var subSequence))
        {
            return null;
        }

        return new ParsedScheduleEntry(
            JobRef: f[0],
            Raw2: NullIfEmpty(f[1]),
            ComponentType: NullIfEmpty(f[2]),
            MemberId: f[3],
            MemberCategory: NullIfEmpty(f[7]),
            LinearLengthM: decimal.TryParse(f[13], NumberStyles.Number, CultureInfo.InvariantCulture, out var len) ? len : null,
            WidthMm: int.TryParse(f[16], out var w) ? w : null,
            HeightMm: int.TryParse(f[17], out var h) ? h : null,
            Done: string.Equals(f[21], "True", StringComparison.OrdinalIgnoreCase),
            EqaValue: eqaValue,
            Batch: batch,
            Sequence: sequence,
            SubSequence: subSequence);
    }

    private static bool TryParseDate(string value, out DateOnly? result)
    {
        if (DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            result = d;
            return true;
        }
        result = null;
        return false;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
