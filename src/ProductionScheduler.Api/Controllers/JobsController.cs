using ProductionScheduler.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;
using ProductionScheduler.Scheduling.BackgroundServices;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/jobs")]
public class JobsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScheduleGenerationService _scheduleGenerationService;
    private readonly IJobStatusService _jobStatusService;

    public JobsController(AppDbContext db, IScheduleGenerationService scheduleGenerationService, IJobStatusService jobStatusService)
    {
        _db = db;
        _scheduleGenerationService = scheduleGenerationService;
        _jobStatusService = jobStatusService;
    }

    // v3.0: the booked delivery date is the deadline; without one the job is TBA and its rough date
    // (est. booked, else expected delivery) stands in.
    private static DateOnly? DueDateOf(Job j) => j.BookedDeliveryDate ?? j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate;

    // Same rule as the scheduler: only a Final Design job with a booked delivery date is called up.
    private static bool IsTba(Job j) => j.BookedDeliveryDate is null || j.DesignStatus != "FinalDesign";

    private async Task<JobDetailDto> ToDetailDtoAsync(Job j) =>
        ToDetailDto(j, (await FreezeWindow.FrozenJobRefsAsync(_db)).Contains(j.JobRef), await _db.JobMembers.CountAsync(m => m.JobRef == j.JobRef));

    private static JobDetailDto ToDetailDto(Job j, bool isFrozen, int memberCount)
    {
        var isLate = JobStatusService.IsLate(DueDateOf(j), j.Status, SiteClock.Now);
        return new JobDetailDto(
            j.Id, j.JobRef, j.ComponentType, j.ClientName, j.SiteAddress, j.SiteSuburb,
            j.SupervisorName, j.SupervisorNumber, j.JobOrderDate, j.JobOrderTime,
            j.TotalEqA, j.CompletedEqA, j.EstimatedStartDate, j.EstimatedCompletionDate,
            j.ExpectedDeliveryDate, j.EstBookedDeliveryDate, j.BookedDeliveryDate,
            j.InvoiceNumber, j.Delivered, j.Status, isLate, j.DesignStatus,
            j.AssignedStationId, j.IsManuallyScheduled, j.ManualStationId, j.ManualScheduleDate, j.SchComponentType, isFrozen, j.ClientRefNumber,
            j.SawBundleStatus, j.SawFileCount, memberCount, IsTba(j));
    }

    [HttpGet]
    public async Task<ActionResult<List<JobSummaryDto>>> GetAll([FromQuery] bool includeArchived = false)
    {
        // Archived jobs (finished and tidied away by nightly maintenance) are history: Admins can list them.
        if (includeArchived && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var jobs = await _db.Jobs
            .Where(j => includeArchived || !j.IsArchived)
            .Include(j => j.AssignedStation)
            .AsNoTracking()
            .ToListAsync();

        var now = SiteClock.Now;
        var atRiskBufferMinutes = await SettingsReader.GetIntAsync(_db, "AtRiskBufferMinutes", 1440);
        var frozen = await FreezeWindow.FrozenJobRefsAsync(_db);
        return jobs.Select(j =>
            {
                var dueDate = DueDateOf(j);
                return new JobSummaryDto(
                    j.Id, j.JobRef, j.ComponentType, j.ClientName, j.AssignedStationId, j.AssignedStation?.Name,
                    dueDate, j.TotalEqA, j.Status,
                    JobStatusService.IsLate(dueDate, j.Status, now),
                    j.EstimatedCompletionDate,
                    JobStatusService.IsAtRisk(j.EstimatedCompletionDate, dueDate, j.Status, now, atRiskBufferMinutes),
                    JobStatusService.IsBehindSchedule(j.EstimatedCompletionDate, j.Status, now),
                    frozen.Contains(j.JobRef), j.SawBundleStatus, j.IsArchived, IsTba(j));
            })
            .ToList();
    }

    [HttpGet("status-refresh")]
    public async Task<ActionResult<object>> StatusRefresh()
    {
        var updated = await _jobStatusService.RefreshAllAsync();
        return new { jobsUpdated = updated };
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobDetailDto>> GetById(int id)
    {
        var job = await _db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
        return job is null ? NotFound() : await ToDetailDtoAsync(job);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UpdateJobResponse>> Create(CreateJobRequest request)
    {
        if (await _db.Jobs.AnyAsync(j => j.JobRef == request.JobRef))
        {
            return Conflict($"Job '{request.JobRef}' already exists.");
        }

        if (await ProductValidation.UnknownProductAsync(_db, request.ComponentType) is { } unknown)
        {
            return BadRequest(unknown);
        }

        var now = SiteClock.Now;
        var job = new Job
        {
            JobRef = request.JobRef,
            ComponentType = request.ComponentType,
            ClientName = request.ClientName,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            EstBookedDeliveryDate = request.EstBookedDeliveryDate,
            ClientRefNumber = request.ClientRefNumber,
            SiteAddress = request.SiteAddress,
            SiteSuburb = request.SiteSuburb,
            SupervisorName = request.SupervisorName,
            SupervisorNumber = request.SupervisorNumber,
            DesignStatus = request.DesignStatus,
            Status = request.DesignStatus == "FinalDesign" ? "ready" : "scheduled",
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, new UpdateJobResponse(await ToDetailDtoAsync(refreshed), result));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UpdateJobResponse>> Update(int id, UpdateJobRequest request)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        if (await ProductValidation.UnknownProductAsync(_db, request.ComponentType) is { } unknown)
        {
            return BadRequest(unknown);
        }

        // The edit form sends the job's complete editable state, so a blank field clears it. The
        // product is the exception: every job needs one, so leaving it blank keeps the current one.
        // (Manually entered fields are never clobbered by re-import, §4.4.)
        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        job.ComponentType = Clean(request.ComponentType) ?? job.ComponentType;
        job.ClientName = Clean(request.ClientName);
        job.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        job.EstBookedDeliveryDate = request.EstBookedDeliveryDate;
        job.ClientRefNumber = Clean(request.ClientRefNumber);
        job.SiteAddress = Clean(request.SiteAddress);
        job.SiteSuburb = Clean(request.SiteSuburb);
        job.SupervisorName = Clean(request.SupervisorName);
        job.SupervisorNumber = Clean(request.SupervisorNumber);
        job.BookedDeliveryDate = request.BookedDeliveryDate;
        job.InvoiceNumber = Clean(request.InvoiceNumber);
        job.Delivered = request.Delivered;
        job.DeliveredAt = request.Delivered && job.DeliveredAt is null ? SiteClock.Now : job.DeliveredAt;
        job.UpdatedAt = SiteClock.Now;

        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.Id == id);
        return new UpdateJobResponse(await ToDetailDtoAsync(refreshed), result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await _db.Jobs.FindAsync(id);
        if (job is null)
        {
            return NotFound();
        }

        // v3.0: a deleted job's MPS is archived rather than left behind in a pool or a station's DataIn.
        await MpsFiles.ArchiveForDeletedJobAsync(_db, job);

        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync();
        await _scheduleGenerationService.GenerateAsync();
        return NoContent();
    }

    [HttpPost("{jobRef}/approve-for-cutting")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<JobDetailDto>> ApproveForCutting(string jobRef, [FromQuery] bool force = false)
    {
        var job = await _db.Jobs.Include(j => j.Members).FirstOrDefaultAsync(j => j.JobRef == jobRef);
        if (job is null)
        {
            return NotFound();
        }

        if (job.Status != "ready")
        {
            return BadRequest($"Job '{jobRef}' is not in 'ready' status (currently '{job.Status}').");
        }

        if (job.BookedDeliveryDate is null)
        {
            return BadRequest($"Job '{jobRef}' is TBA: enter its booked delivery date before it can be cut.");
        }

        var settings = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        var doNotCutBase = settings.GetValueOrDefault("SawFilesDoNotCutPath");
        var forCuttingBase = settings.GetValueOrDefault("SawFilesForCuttingPath");
        if (string.IsNullOrWhiteSpace(doNotCutBase) || string.IsNullOrWhiteSpace(forCuttingBase))
        {
            return BadRequest("SawFilesDoNotCutPath / SawFilesForCuttingPath are not configured in Settings.");
        }

        // A complete bundle is in the waiting folder (§5.11). One still being collected is only sent to the
        // saw when the Admin confirms it (force) — e.g. a member that legitimately has no saw file.
        var sourceDir = Path.Combine(doNotCutBase, jobRef);
        var destDir = Path.Combine(forCuttingBase, jobRef);
        var rawFilesBase = settings.GetValueOrDefault("RawFilesPath");
        var stagingDir = string.IsNullOrWhiteSpace(rawFilesBase) ? null : Path.Combine(rawFilesBase, MmeFileWatcherService.StagingFolderName, jobRef);

        if (!Directory.Exists(sourceDir))
        {
            if (stagingDir is null || !Directory.Exists(stagingDir))
            {
                return BadRequest($"No saw files have arrived for '{jobRef}' yet, so there is nothing to send to the saw.");
            }

            sourceDir = stagingDir;
        }

        // Checked on the files themselves, wherever they are: a waiting folder can hold a partial set too.
        var files = Directory.GetFiles(sourceDir, "*.mme");
        if (!force && !MmeFileWatcherService.IsComplete(job, files))
        {
            return Conflict($"The saw files for '{jobRef}' are incomplete ({files.Length} file(s) for {job.Members.Count} member(s)). Approve anyway?");
        }

        if (Directory.Exists(destDir))
        {
            return BadRequest($"'{destDir}' already exists, so the saw files can't be moved there. Clear it first.");
        }

        // File by file (the waiting and cutting folders may be on different drives or shares, where a
        // directory move fails); if one fails, the ones already moved go back so nothing changes.
        var moved = new List<(string From, string To)>();
        try
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var to = Path.Combine(destDir, Path.GetFileName(file));
                System.IO.File.Move(file, to);
                moved.Add((file, to));
            }

            if (Directory.GetFileSystemEntries(sourceDir).Length == 0)
            {
                Directory.Delete(sourceDir);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            foreach (var (from, to) in moved)
            {
                try { System.IO.File.Move(to, from); } catch (IOException) { /* reported below */ }
            }

            if (Directory.Exists(destDir) && Directory.GetFileSystemEntries(destDir).Length == 0)
            {
                Directory.Delete(destDir);
            }

            return BadRequest($"Failed to move the saw files to the cutting folder: {ex.Message}");
        }

        job.SawBundleStatus = "cutting";

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        job.Status = "approved";
        job.ApprovedForCuttingAt = SiteClock.Now;
        job.ApprovedByUserId = userId;
        job.UpdatedAt = SiteClock.Now;
        await _db.SaveChangesAsync();

        return await ToDetailDtoAsync(job);
    }

    [HttpPost("{jobRef}/schedule-manually")]
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<ActionResult<UpdateJobResponse>> ScheduleManually(string jobRef, ScheduleManuallyRequest request)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef);
        if (job is null)
        {
            return NotFound();
        }

        // Only work that hasn't started can be moved: the scheduler re-plans 'ready'/'approved'/'cut'
        // jobs, but a running job stays where it is, and a quote isn't scheduled at all. Saving a pin it
        // would then ignore just makes a drag look like it silently failed.
        if (job.DesignStatus != "FinalDesign" || job.Status is not ("ready" or "approved" or "cut"))
        {
            return BadRequest(job.Status == "in-production"
                ? $"Job '{jobRef}' is already in production and can't be moved."
                : $"Job '{jobRef}' can't be scheduled while it is '{job.Status}'.");
        }

        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        if (calendar.FreezeUntil(SiteClock.Now) is DateTime freezeUntil)
        {
            var lastFrozenDay = freezeUntil.AddDays(-1);
            if ((await FreezeWindow.FrozenJobRefsAsync(_db)).Contains(jobRef))
            {
                return BadRequest($"Job '{jobRef}' starts inside the freeze window (through {lastFrozenDay:ddd d MMM}) and can't be moved.");
            }

            if (request.ScheduleDate.ToDateTime(TimeOnly.MinValue) < freezeUntil)
            {
                return BadRequest($"{request.ScheduleDate:ddd d MMM} is inside the freeze window (through {lastFrozenDay:ddd d MMM}) — choose a later day.");
            }
        }

        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.StationId && s.IsActive);
        if (station is null)
        {
            return BadRequest($"Station {request.StationId} does not exist or is inactive.");
        }

        // §4.4: a job can only run on a station whose ComponentType matches its own.
        if (station.ComponentType != job.ComponentType)
        {
            return BadRequest(
                $"Station '{station.Name}' handles '{station.ComponentType}' but job '{jobRef}' is '{job.ComponentType}'.");
        }

        job.IsManuallyScheduled = true;
        job.ManualStationId = request.StationId;
        job.ManualScheduleDate = request.ScheduleDate;
        job.UpdatedAt = SiteClock.Now;
        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        return new UpdateJobResponse(await ToDetailDtoAsync(refreshed), result);
    }

    [HttpDelete("{jobRef}/schedule-manually")]
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<ActionResult<UpdateJobResponse>> UnpinSchedule(string jobRef)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef);
        if (job is null)
        {
            return NotFound();
        }

        job.IsManuallyScheduled = false;
        job.ManualStationId = null;
        job.ManualScheduleDate = null;
        job.UpdatedAt = SiteClock.Now;
        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        return new UpdateJobResponse(await ToDetailDtoAsync(refreshed), result);
    }
}
