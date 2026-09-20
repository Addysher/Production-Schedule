using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;

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
        var isLate = JobStatusService.IsLate(j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate, j.Status, DateTime.UtcNow);
        return new JobDetailDto(
            j.Id, j.JobRef, j.ComponentType, j.ClientName, j.SiteAddress, j.SiteSuburb,
            j.SupervisorName, j.SupervisorNumber, j.JobOrderDate, j.JobOrderTime,
            j.TotalEqA, j.EstimatedStartDate, j.EstimatedCompletionDate,
            j.ExpectedDeliveryDate, j.EstBookedDeliveryDate, j.BookedDeliveryDate,
            j.InvoiceNumber, j.Delivered, j.Status, isLate, j.DesignStatus,
            j.AssignedStationId, j.IsManuallyScheduled, j.ManualStationId, j.ManualScheduleDate);
    }

    [HttpGet]
    public async Task<ActionResult<List<JobSummaryDto>>> GetAll()
    {
        var jobs = await _db.Jobs
            .Where(j => !j.IsArchived)
            .Include(j => j.AssignedStation)
            .AsNoTracking()
            .ToListAsync();

        var now = DateTime.UtcNow;
        return jobs.Select(j => new JobSummaryDto(
                j.Id, j.JobRef, j.ComponentType, j.ClientName, j.AssignedStationId, j.AssignedStation?.Name,
                j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate, j.TotalEqA, j.Status,
                JobStatusService.IsLate(j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate, j.Status, now)))
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

        var now = DateTime.UtcNow;
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
        job.DeliveredAt = request.Delivered && job.DeliveredAt is null ? DateTime.UtcNow : job.DeliveredAt;
        job.UpdatedAt = DateTime.UtcNow;

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
        job.ApprovedForCuttingAt = DateTime.UtcNow;
        job.ApprovedByUserId = userId;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return ToDetailDto(job);
    }

    [HttpPost("{jobRef}/schedule-manually")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UpdateJobResponse>> ScheduleManually(string jobRef, ScheduleManuallyRequest request)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.JobRef == jobRef);
        if (job is null)
        {
            return NotFound();
        }

        if (!await _db.Stations.AnyAsync(s => s.Id == request.StationId && s.IsActive))
        {
            return BadRequest($"Station {request.StationId} does not exist or is inactive.");
        }

        job.IsManuallyScheduled = true;
        job.ManualStationId = request.StationId;
        job.ManualScheduleDate = request.ScheduleDate;
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        return new UpdateJobResponse(ToDetailDto(refreshed), result);
    }

    [HttpDelete("{jobRef}/schedule-manually")]
    [Authorize(Roles = "Admin")]
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
        job.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var result = await _scheduleGenerationService.GenerateAsync();
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        return new UpdateJobResponse(ToDetailDto(refreshed), result);
    }
}
