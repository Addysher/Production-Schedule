using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/jobtypes")]
public class JobTypesController : ControllerBase
{
    private readonly AppDbContext _db;

    public JobTypesController(AppDbContext db)
    {
        _db = db;
    }

    private static JobTypeDto ToDto(JobType jt) => new(jt.Id, jt.Name, jt.ComponentType, jt.MinsPerUnit, jt.IsActive);

    // Read access is open to any authenticated user — the Quote Estimator (§5.5) needs the job type
    // list for its dropdown and is available to any logged-in user, not just Admins.
    [HttpGet]
    public async Task<ActionResult<List<JobTypeDto>>> GetAll()
    {
        var jobTypes = await _db.JobTypes.AsNoTracking().OrderBy(jt => jt.Name).ToListAsync();
        return jobTypes.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<JobTypeDto>> GetById(int id)
    {
        var jobType = await _db.JobTypes.AsNoTracking().FirstOrDefaultAsync(jt => jt.Id == id);
        return jobType is null ? NotFound() : ToDto(jobType);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<JobTypeDto>> Create(UpsertJobTypeRequest request)
    {
        var jobType = new JobType
        {
            Name = request.Name,
            ComponentType = request.ComponentType,
            MinsPerUnit = request.MinsPerUnit,
            IsActive = request.IsActive
        };
        _db.JobTypes.Add(jobType);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = jobType.Id }, ToDto(jobType));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<JobTypeDto>> Update(int id, UpsertJobTypeRequest request)
    {
        var jobType = await _db.JobTypes.FindAsync(id);
        if (jobType is null)
        {
            return NotFound();
        }

        jobType.Name = request.Name;
        jobType.ComponentType = request.ComponentType;
        jobType.MinsPerUnit = request.MinsPerUnit;
        jobType.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return ToDto(jobType);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var jobType = await _db.JobTypes.FindAsync(id);
        if (jobType is null)
        {
            return NotFound();
        }

        jobType.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
