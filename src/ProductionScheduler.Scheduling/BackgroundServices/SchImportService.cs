using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.8 — watches every active Product's pool folder for new .sch files (semicolon-delimited,
// dd/mm/yyyy dates) and runs the two-stage quote / finaldesign import workflow.
//
// v3.0: the pool a file is dropped into decides the job's product, not the file's own component
// type (which is kept as Jobs.SchComponentType). Once read, the file is archived into the product's
// archive folder (or a "_processed" subfolder of the pool if none is set) and kept; a rejected file
// goes to the pool's "_errors" subfolder. Either way it is never read twice.
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

        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.PoolPath != null && p.PoolPath != "").ToListAsync(ct);
        var settleSeconds = await SettingsReader.GetIntAsync(db, "SchFileSettleSeconds", 5, ct);
        var anyImported = false;

        foreach (var product in products)
        {
            if (!Directory.Exists(product.PoolPath))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(product.PoolPath!, "*.sch"))
            {
                // Sapphire may still be writing the file; reading half of it would be rejected as malformed.
                if (!IsSettled(file, settleSeconds))
                {
                    continue;
                }

                if (await ProcessFileAsync(db, product, file, ct))
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

    private async Task<bool> ProcessFileAsync(AppDbContext db, Product product, string filePath, CancellationToken ct)
    {
        var lines = await File.ReadAllLinesAsync(filePath, ct);
        var parsed = ParseFile(lines);

        if (parsed.Problems.Count > 0 || parsed.ScheduleJob is null)
        {
            var shown = string.Join("; ", parsed.Problems.Take(4));
            var more = parsed.Problems.Count > 4 ? $" (+{parsed.Problems.Count - 4} more)" : "";
            await RejectAsync(db, product, filePath, parsed.ScheduleJob?.JobRef, $"File rejected, nothing imported — {shown}{more}", ct);
            return false;
        }

        var job = parsed.ScheduleJob;

        var existing = await db.Jobs.FirstOrDefaultAsync(j => j.JobRef == job.JobRef, ct);
        var isFinalDesign = string.Equals(job.DesignStatus, "FinalDesign", StringComparison.OrdinalIgnoreCase);

        if (!isFinalDesign)
        {
            if (existing is not null)
            {
                await RejectAsync(db, product, filePath, job.JobRef, "A quote for this job was already imported", ct);
                return false;
            }

            await CreateJobAsync(db, product, job, parsed.ScheduleEntries, filePath, ct);
        }
        else if (existing is null)
        {
            // finaldesign arriving with no prior quote — treated as a brand new job, all fields set.
            await CreateJobAsync(db, product, job, parsed.ScheduleEntries, filePath, ct);
        }
        else
        {
            await UpdateJobAsync(db, existing, product, job, parsed.ScheduleEntries, filePath, ct);
        }

        await db.SaveChangesAsync(ct);

        // v3.0: the SCH file is read in, then archived (kept) in the product's archive folder.
        var archivedPath = string.IsNullOrWhiteSpace(product.ArchivePath)
            ? MoveTo(filePath, Path.Combine(product.PoolPath!, "_processed"))
            : MoveTo(filePath, product.ArchivePath);
        await db.Jobs.Where(j => j.JobRef == job.JobRef)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.SchSourcePath, archivedPath), ct);

        // The pool decides the product; if the file itself says something different, note it so a
        // file dropped in the wrong pool can be spotted.
        var mismatch = SameProduct(job.ComponentType, product.Name) ? "" : $" (the file says '{job.ComponentType}')";
        MaintenanceLogWriter.Add(db, "SchImport", "success",
            $"Imported {job.DesignStatus} as {product.Name}{mismatch} — {parsed.ScheduleEntries.Count} members, {parsed.ScheduleEntries.Sum(e => e.EqaValue)} EqA",
            job.JobRef, archivedPath);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported SCH file {File} for job {JobRef} ({Stage}).", filePath, job.JobRef, job.DesignStatus);
        return true;
    }

    private async Task RejectAsync(AppDbContext db, Product product, string filePath, string? jobRef, string reason, CancellationToken ct)
    {
        _logger.LogError("SCH file {File} rejected: {Reason}", filePath, reason);
        var errorsPath = MoveTo(filePath, Path.Combine(product.PoolPath!, "_errors"));
        MaintenanceLogWriter.Add(db, "SchImport", "error", reason, jobRef, errorsPath);
        await db.SaveChangesAsync(ct);
    }

    // "RoofTrusses" in the file vs a product named "Roof Trusses" is the same thing.
    private static bool SameProduct(string? schComponentType, string productName) =>
        string.Equals(schComponentType?.Replace(" ", ""), productName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);

    private static async Task CreateJobAsync(
        AppDbContext db, Product product, ParsedScheduleJob job, List<ParsedScheduleEntry> entries, string filePath, CancellationToken ct)
    {
        var now = SiteClock.Now;
        var entity = new Job
        {
            JobRef = job.JobRef,
            ComponentType = product.Name,
            SchComponentType = job.ComponentType,
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
            MpsSourcePath = product.PoolPath, // the MPS waits in the same pool folder as the SCH
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
        AppDbContext db, Job existing, Product product, ParsedScheduleJob job, List<ParsedScheduleEntry> entries, string filePath, CancellationToken ct)
    {
        // JobOrderDate/Time and manually-entered fields (SupervisorName/Number, BookedDeliveryDate,
        // InvoiceNumber, Delivered) are never touched by a re-import (§4.4).
        existing.ComponentType = product.Name;
        existing.SchComponentType = job.ComponentType;
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
        // Only a quote-stage job becomes 'ready'. A revised Final Design for a job that is already
        // approved, cut, running or done must not knock it back down the workflow — the batchcut
        // watcher and PRD reader own those later transitions.
        if (existing.Status == "scheduled")
        {
            existing.Status = "ready";
        }
        existing.SchSourcePath = filePath;
        existing.MpsSourcePath = product.PoolPath;
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
                member.SetupEqA = entry.SetupEqA;
                member.ProcessEqA = entry.ProcessEqA;
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
        SetupEqA = entry.SetupEqA,
        ProcessEqA = entry.ProcessEqA,
        WidthMm = entry.WidthMm,
        HeightMm = entry.HeightMm,
        Batch = entry.Batch,
        Sequence = entry.Sequence,
        SubSequence = entry.SubSequence,
        EqaValue = entry.EqaValue,
        Done = entry.Done,
        Raw2 = entry.Raw2
    };

    private static string MoveTo(string filePath, string destDir)
    {
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
        decimal SetupEqA, decimal ProcessEqA, int? WidthMm, int? HeightMm, bool Done, decimal EqaValue,
        int Batch, int Sequence, int SubSequence);

    // A ScheduleJob line has exactly 16 positions; a ScheduleEntry line has 27 or more (several
    // positions between the documented ones are unlabelled MiTek fields we don't persist).
    //
    // Checked against real Sapphire exports (not just the design doc's worked examples), two things
    // the original version of this parser got wrong:
    //  1. Every data line is prefixed with its record-type label — "ScheduleJob: " or
    //     "ScheduleEntry: " — which a naive `line.Split(';')` glues onto field 1 (JobRef would come
    //     out as "ScheduleJob: 58749MPF"). Record type is identified by that label, and the label is
    //     stripped before the remainder is split into positional fields.
    //  2. Real files always open with four file-level header lines with their own, unrelated field
    //     counts — "File Version=", "Production Units Names=", "Batchcut:", "Activities:" — none of
    //     which are data records. They are recognised by prefix and skipped.
    // Real files also delimit with "; " (a space after each semicolon), so string fields are trimmed
    // (see NullIfEmpty and Field below) rather than carrying a leading space into the database.
    //
    // A third thing the original version got wrong, found by checking against three real jobs whose
    // true "Production Value (EqA)" totals were independently known: the design doc's worked example
    // put that value at field 23, but summing field 23 across a job's members doesn't reproduce its
    // known total for any of the three real files. Field 23 is something else — its real meaning is
    // unknown, and it is left unread. The true total is the sum, across every member, of field 13 plus
    // field 14 — the two Sapphire "Activities" this file's header names (e.g. "ProcessJig; SetupJig"):
    // field 13 is the SetupJig contribution (0 for a component type with only one activity, such as
    // WallFrames' single "FramingMachine"), field 14 is ProcessJig's. Verified exactly against
    // 58749MPF (129.19) and 60656MTG (158.11); 60497MWG (38.63) rounds to the user's recollection of 38.
    //
    // Deliberately strict otherwise: any line that isn't a recognised record or header is reported in
    // Problems and the caller rejects the whole file. A partial import (a job with missing members and
    // a wrong TotalEqA that can never complete) is far worse than a clear "line 7: ..." message an
    // administrator can act on.
    internal static ParsedFile ParseFile(IEnumerable<string> lines)
    {
        ParsedScheduleJob? scheduleJob = null;
        var entries = new List<ParsedScheduleEntry>();
        var problems = new List<string>();
        var lineNo = 0;

        foreach (var rawLine in lines)
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryStripLabel(line, "ScheduleJob:", out var jobContent))
            {
                if (scheduleJob is not null)
                {
                    problems.Add($"line {lineNo}: a second ScheduleJob record (only one per file is expected)");
                    continue;
                }

                var fields = jobContent.Split(';');
                if (fields.Length != 16)
                {
                    problems.Add($"line {lineNo}: ScheduleJob has {fields.Length} fields — expected 16");
                    continue;
                }

                scheduleJob = TryParseScheduleJob(fields, lineNo, problems);
            }
            else if (TryStripLabel(line, "ScheduleEntry:", out var entryContent))
            {
                var fields = entryContent.Split(';');
                if (fields.Length < 27)
                {
                    problems.Add($"line {lineNo}: ScheduleEntry has {fields.Length} fields — expected 27+");
                    continue;
                }

                var entry = TryParseScheduleEntry(fields, lineNo, problems);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }
            else if (IsHeaderLine(line))
            {
                continue; // Sapphire file-level metadata — not a data record.
            }
            else
            {
                problems.Add($"line {lineNo}: unrecognised line with {line.Split(';').Length} fields — expected 'ScheduleJob:', 'ScheduleEntry:', or a known header line");
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

        decimal Decimal(int position, string name)
        {
            if (decimal.TryParse(Field(position), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            problems.Add($"line {lineNo}: field {position} ({name}) '{Field(position)}' is not a number");
            return 0;
        }

        var setupEqa = Decimal(13, "SetupJig EqA");
        var processEqa = Decimal(14, "ProcessJig EqA");

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
            SetupEqA: setupEqa,
            ProcessEqA: processEqa,
            WidthMm: int.TryParse(f[16], out var w) ? w : null,
            HeightMm: int.TryParse(f[17], out var h) ? h : null,
            Done: string.Equals(f[21].Trim(), "True", StringComparison.OrdinalIgnoreCase),
            EqaValue: setupEqa + processEqa,
            Batch: batch,
            Sequence: sequence,
            SubSequence: subSequence);
    }

    private static bool TryStripLabel(string line, string label, out string content)
    {
        if (line.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            content = line[label.Length..];
            return true;
        }

        content = "";
        return false;
    }

    private static bool IsHeaderLine(string line) =>
        line.StartsWith("File Version", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Production Units Names", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Batchcut:", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("Activities:", StringComparison.OrdinalIgnoreCase);

    // Real files delimit with "; " — a space after every semicolon — so a raw 0-based field access
    // (used for the several Raw*/descriptive fields below that aren't run through Field()'s .Trim())
    // would otherwise carry that leading space into the database (e.g. ClientName == " I-Smart").
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
