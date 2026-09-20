using ProductionScheduler.Domain;
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
        var settleSeconds = await SettingsReader.GetIntAsync(db, "SchFileSettleSeconds", 5, ct);
        var anyImported = false;

        foreach (var importPath in importPaths)
        {
            if (!Directory.Exists(importPath.UncPath))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(importPath.UncPath, "*.sch"))
            {
                // Sapphire may still be writing the file; reading half of it would be rejected as malformed.
                if (!IsSettled(file, settleSeconds))
                {
                    continue;
                }

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

    // File timestamps are compared in UTC on purpose — this is about the file's age, not a wall-clock reading.
    private static bool IsSettled(string path, int settleSeconds)
    {
        try
        {
            if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(path)).TotalSeconds < settleSeconds)
            {
                return false;
            }

            using var exclusive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false; // still open in the writer; try again next poll
        }
    }

    private async Task<bool> ProcessFileAsync(AppDbContext db, SchImportPath importPath, string filePath, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var parsed = ParseFile(lines);

        if (parsed.Problems.Count > 0 || parsed.ScheduleJob is null)
        {
            var shown = string.Join("; ", parsed.Problems.Take(4));
            var more = parsed.Problems.Count > 4 ? $" (+{parsed.Problems.Count - 4} more)" : "";
            await RejectAsync(db, importPath, filePath, parsed.ScheduleJob?.JobRef, $"File rejected, nothing imported — {shown}{more}", ct);
            return false;
        }

        var job = parsed.ScheduleJob;

        if (importPath.ComponentType is not null && !string.Equals(importPath.ComponentType, job.ComponentType, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(db, importPath, filePath, job.JobRef,
                $"Component type '{job.ComponentType}' does not match import path '{importPath.Name}', which only accepts '{importPath.ComponentType}' (wrong folder?)", ct);
            return false;
        }

        var existing = await db.Jobs.FirstOrDefaultAsync(j => j.JobRef == job.JobRef, ct);
        var isFinalDesign = string.Equals(job.DesignStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase);

        if (!isFinalDesign)
        {
            if (existing is not null)
            {
                await RejectAsync(db, importPath, filePath, job.JobRef, "A quote for this job was already imported", ct);
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

        MaintenanceLogWriter.Add(db, "SchImport", "success",
            $"Imported {job.DesignStatus} — {parsed.ScheduleEntries.Count} members, {parsed.ScheduleEntries.Sum(e => e.EqaValue)} EqA",
            job.JobRef, processedPath);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported SCH file {File} for job {JobRef} ({Stage}).", filePath, job.JobRef, job.DesignStatus);
        return true;
    }

    private async Task RejectAsync(AppDbContext db, SchImportPath importPath, string filePath, string? jobRef, string reason, CancellationToken ct)
    {
        _logger.LogError("SCH file {File} rejected: {Reason}", filePath, reason);
        var errorsPath = MoveTo(filePath, importPath.UncPath, "_errors");
        MaintenanceLogWriter.Add(db, "SchImport", "error", reason, jobRef, errorsPath);
        await db.SaveChangesAsync(ct);
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
        var now = SiteClock.Now;
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
        existing.UpdatedAt = SiteClock.Now;

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
            destFile = Path.Combine(destDir, $"{Path.GetFileNameWithoutExtension(filePath)}_{SiteClock.Now:yyyyMMddHHmmssfff}{Path.GetExtension(filePath)}");
        }
        File.Move(filePath, destFile);
        return destFile;
    }

    internal record ParsedFile(ParsedScheduleJob? ScheduleJob, List<ParsedScheduleEntry> ScheduleEntries, List<string> Problems);

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
    //
    // Deliberately strict. This parser has only ever been checked against the design doc's examples, so
    // any line it cannot fully understand is reported in Problems and the caller rejects the whole file.
    // A partial import (a job with missing members and a wrong TotalEqA that can never complete) is far
    // worse than a clear "line 7: ..." message an administrator can act on.
    internal static ParsedFile ParseFile(IEnumerable<string> lines)
    {
        ParsedScheduleJob? scheduleJob = null;
        var entries = new List<ParsedScheduleEntry>();
        var problems = new List<string>();
        var lineNo = 0;

        foreach (var rawLine in lines)
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var fields = rawLine.Split(';');

            if (fields.Length == 16)
            {
                if (scheduleJob is not null)
                {
                    problems.Add($"line {lineNo}: a second ScheduleJob record (only one per file is expected)");
                    continue;
                }

                scheduleJob = TryParseScheduleJob(fields, lineNo, problems);
            }
            else if (fields.Length >= 27)
            {
                var entry = TryParseScheduleEntry(fields, lineNo, problems);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            else
            {
                problems.Add($"line {lineNo}: {fields.Length} fields — expected 16 (ScheduleJob) or 27+ (ScheduleEntry)");
            }
        }

        if (scheduleJob is null && problems.Count == 0)
        {
            problems.Add("no ScheduleJob record found");
        }

        if (entries.Count == 0)
        {
            problems.Add("no ScheduleEntry (member) records found");
        }

        if (scheduleJob is not null)
        {
            foreach (var other in entries.Where(e => e.JobRef != scheduleJob.JobRef).Select(e => e.JobRef).Distinct())
            {
                problems.Add($"member records reference job '{other}' but the file's job is '{scheduleJob.JobRef}'");
            }
        }

        foreach (var duplicate in entries.GroupBy(e => e.MemberId).Where(g => g.Count() > 1))
        {
            problems.Add($"member '{duplicate.Key}' appears {duplicate.Count()} times");
        }

        return new ParsedFile(scheduleJob, entries, problems);
    }

    private static ParsedScheduleJob? TryParseScheduleJob(string[] f, int lineNo, List<string> problems)
    {
        var before = problems.Count;
        string Field(int position) => f[position - 1].Trim();

        if (Field(1).Length == 0)
        {
            problems.Add($"line {lineNo}: field 1 (Job Reference) is empty");
        }

        if (Field(3).Length == 0)
        {
            problems.Add($"line {lineNo}: field 3 (Component Type) is empty");
        }

        DateOnly? Date(int position, string name)
        {
            var value = Field(position);
            if (value.Length == 0)
            {
                return null;
            }

            if (DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                return parsed;
            }

            problems.Add($"line {lineNo}: field {position} ({name}) '{value}' is not a dd/MM/yyyy date");
            return null;
        }

        var expectedDelivery = Date(5, "Expected Delivery Date");
        var estBookedDelivery = Date(6, "Est. Booked Delivery Date");
        var jobOrderDate = Date(14, "Job Order Date");

        TimeOnly? jobOrderTime = null;
        if (Field(15).Length > 0)
        {
            if (TimeOnly.TryParse(Field(15), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                jobOrderTime = time;
            }
            else
            {
                problems.Add($"line {lineNo}: field 15 (Job Order Time) '{Field(15)}' is not a time");
            }
        }

        var designStatus = Field(16);
        if (string.Equals(designStatus, "Quote", StringComparison.OrdinalIgnoreCase))
        {
            designStatus = "Quote";
        }
        else if (string.Equals(designStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase))
        {
            designStatus = "FinalDesign";
        }
        else
        {
            problems.Add($"line {lineNo}: field 16 (Design Status) '{designStatus}' must be 'quote' or 'finaldesign'");
        }

        if (problems.Count > before)
        {
            return null;
        }

        return new ParsedScheduleJob(
            JobRef: Field(1),
            Raw2: NullIfEmpty(f[1]),
            ComponentType: Field(3),
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

    private static ParsedScheduleEntry? TryParseScheduleEntry(string[] f, int lineNo, List<string> problems)
    {
        var before = problems.Count;
        string Field(int position) => f[position - 1].Trim();

        if (Field(1).Length == 0)
        {
            problems.Add($"line {lineNo}: field 1 (Job Reference) is empty");
        }

        if (Field(4).Length == 0)
        {
            problems.Add($"line {lineNo}: field 4 (Member ID) is empty");
        }

        if (!decimal.TryParse(Field(23), NumberStyles.Number, CultureInfo.InvariantCulture, out var eqaValue))
        {
            problems.Add($"line {lineNo}: field 23 (Production Value EqA) '{Field(23)}' is not a number");
        }

        int Whole(int position, string name)
        {
            if (int.TryParse(Field(position), out var value))
            {
                return value;
            }

            problems.Add($"line {lineNo}: field {position} ({name}) '{Field(position)}' is not a whole number");
            return 0;
        }

        var batch = Whole(25, "Batch");
        var sequence = Whole(26, "Sequence");
        var subSequence = Whole(27, "Sub-sequence");

        if (problems.Count > before)
        {
            return null;
        }

        return new ParsedScheduleEntry(
            JobRef: Field(1),
            Raw2: NullIfEmpty(f[1]),
            ComponentType: NullIfEmpty(f[2]),
            MemberId: Field(4),
            MemberCategory: NullIfEmpty(f[7]),
            LinearLengthM: decimal.TryParse(f[13], NumberStyles.Number, CultureInfo.InvariantCulture, out var len) ? len : null,
            WidthMm: int.TryParse(f[16], out var w) ? w : null,
            HeightMm: int.TryParse(f[17], out var h) ? h : null,
            Done: string.Equals(f[21].Trim(), "True", StringComparison.OrdinalIgnoreCase),
            EqaValue: eqaValue,
            Batch: batch,
            Sequence: sequence,
            SubSequence: subSequence);
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
