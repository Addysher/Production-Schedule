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

    private static JobDetailDto ToDetailDto(Job j)
    {
        var isLate = JobStatusService.IsLate(j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate, j.Status, SiteClock.Now);
        return new JobDetailDto(
            j.Id, j.JobRef, j.ComponentType, j.ClientName, j.SiteAddress, j.SiteSuburb,
            j.SupervisorName, j.SupervisorNumber, j.JobOrderDate, j.JobOrderTime,
            j.TotalEqA, j.CompletedEqA, j.EstimatedStartDate, j.EstimatedCompletionDate,
            j.ExpectedDeliveryDate, j.EstBookedDeliveryDate, j.BookedDeliveryDate,
            j.InvoiceNumber, j.Delivered, j.Status, isLate, j.DesignStatus,
            j.AssignedStationId, j.IsManuallyScheduled, j.ManualStationId, j.ManualScheduleDate, j.SchComponentType);
    }

    [HttpGet]
    public async Task<ActionResult<List<JobSummaryDto>>> GetAll()
    {
        var jobs = await _db.Jobs
            .Where(j => !j.IsArchived)
            .Include(j => j.AssignedStation)
            .AsNoTracking()
            .ToListAsync();

        var now = SiteClock.Now;
        var atRiskBufferMinutes = await SettingsReader.GetIntAsync(_db, "AtRiskBufferMinutes", 1440);
        return jobs.Select(j =>
            {
                var dueDate = j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate;
                return new JobSummaryDto(
                    j.Id, j.JobRef, j.ComponentType, j.ClientName, j.AssignedStationId, j.AssignedStation?.Name,
                    dueDate, j.TotalEqA, j.Status,
                    JobStatusService.IsLate(dueDate, j.Status, now),
                    j.EstimatedCompletionDate,
                    JobStatusService.IsAtRisk(j.EstimatedCompletionDate, dueDate, j.Status, now, atRiskBufferMinutes),
                    JobStatusService.IsBehindSchedule(j.EstimatedCompletionDate, j.Status, now));
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
        return job is null ? NotFound() : ToDetailDto(job);
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
        return CreatedAtAction(nameof(GetById), new { id = job.Id }, new UpdateJobResponse(ToDetailDto(refreshed), result));
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

        // Manually entered fields are never clobbered by re-import (§4.4) — here they're the
        // fields the edit modal is allowed to touch at all.
        job.ComponentType = request.ComponentType ?? job.ComponentType;
        job.ClientName = request.ClientName ?? job.ClientName;
        job.ExpectedDeliveryDate = request.ExpectedDeliveryDate ?? job.ExpectedDeliveryDate;
        job.EstBookedDeliveryDate = request.EstBookedDeliveryDate ?? job.EstBookedDeliveryDate;
        job.ClientRefNumber = request.ClientRefNumber ?? job.ClientRefNumber;
        job.SiteAddress = request.SiteAddress ?? job.SiteAddress;
        job.SiteSuburb = request.SiteSuburb ?? job.SiteSuburb;
        job.SupervisorName = request.SupervisorName ?? job.SupervisorName;
        job.SupervisorNumber = request.SupervisorNumber ?? job.SupervisorNumber;
        job.BookedDeliveryDate = request.BookedDeliveryDate ?? job.BookedDeliveryDate;
        job.InvoiceNumber = request.InvoiceNumber ?? job.InvoiceNumber;
        job.Delivered = request.Delivered;
        job.DeliveredAt = request.Delivered && job.DeliveredAt is null ? SiteClock.Now : job.DeliveredAt;
        job.UpdatedAt = SiteClock.Now;

        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.Id == id);
        return new UpdateJobResponse(ToDetailDto(refreshed), result);
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
    public async Task<ActionResult<JobDetailDto>> ApproveForCutting(string jobRef)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef);
        if (job is null)
        {
            return NotFound();
        }

        if (job.Status != "ready")
        {
            return BadRequest($"Job '{jobRef}' is not in 'ready' status (currently '{job.Status}').");
        }

        var settings = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value);
        var doNotCutBase = settings.GetValueOrDefault("SawFilesDoNotCutPath");
        var forCuttingBase = settings.GetValueOrDefault("SawFilesForCuttingPath");
        if (string.IsNullOrWhiteSpace(doNotCutBase) || string.IsNullOrWhiteSpace(forCuttingBase))
        {
            return BadRequest("SawFilesDoNotCutPath / SawFilesForCuttingPath are not configured in Settings.");
        }

        var sourceDir = Path.Combine(doNotCutBase, jobRef);
        var destDir = Path.Combine(forCuttingBase, jobRef);

        if (!Directory.Exists(sourceDir))
        {
            return BadRequest($"Bundle folder for '{jobRef}' was not found at '{sourceDir}'. The MME bundler may not have run yet.");
        }

        try
        {
            Directory.Move(sourceDir, destDir);
        }
        catch (IOException ex)
        {
            return BadRequest($"Failed to move job folder to the cutting path: {ex.Message}");
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        job.Status = "approved";
        job.ApprovedForCuttingAt = SiteClock.Now;
        job.ApprovedByUserId = userId;
        job.UpdatedAt = SiteClock.Now;
        await _db.SaveChangesAsync();

        return ToDetailDto(job);
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
        return new UpdateJobResponse(ToDetailDto(refreshed), result);
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
        return new UpdateJobResponse(ToDetailDto(refreshed), result);
    }
}
