using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/stations")]
public class StationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StationsController(AppDbContext db)
    {
        _db = db;
    }

    private static StationDto ToDto(Station s) =>
        new(s.Id, s.Name, s.ComponentType, s.UncPath, s.IsActive, s.DisplayOrder, s.DailyEqATarget, s.ArchivePath);

    // Read access is open to any authenticated user — the calendar and dashboard (Viewer-visible)
    // render station columns/cards, even though station management itself is Admin-only.
    [HttpGet]
    public async Task<ActionResult<List<StationDto>>> GetAll()
    {
        var stations = await _db.Stations.AsNoTracking().OrderBy(s => s.DisplayOrder).ToListAsync();
        return stations.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StationDto>> GetById(int id)
    {
        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return station is null ? NotFound() : ToDto(station);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StationDto>> Create(CreateStationRequest request)
    {
        var station = new Station
        {
            Name = request.Name,
            ComponentType = request.ComponentType,
            UncPath = request.UncPath,
            DisplayOrder = request.DisplayOrder,
            DailyEqATarget = request.DailyEqATarget,
            ArchivePath = request.ArchivePath,
            IsActive = true
        };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = station.Id }, ToDto(station));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StationDto>> Update(int id, UpdateStationRequest request)
    {
        var station = await _db.Stations.FindAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        station.Name = request.Name;
        station.ComponentType = request.ComponentType;
        station.UncPath = request.UncPath;
        station.IsActive = request.IsActive;
        station.DisplayOrder = request.DisplayOrder;
        station.DailyEqATarget = request.DailyEqATarget;
        station.ArchivePath = request.ArchivePath;
        await _db.SaveChangesAsync();
        return ToDto(station);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var station = await _db.Stations.FindAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        // Deactivate rather than hard-delete — stations are referenced by historical ScheduleItems.
        station.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id:int}/capacity-overrides")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<CapacityOverrideDto>>> GetCapacityOverrides(int id)
    {
        var overrides = await _db.StationCapacityOverrides
            .AsNoTracking()
            .Where(o => o.StationId == id)
            .OrderBy(o => o.OverrideDate)
            .ToListAsync();
        return overrides.Select(o => new CapacityOverrideDto(o.Id, o.StationId, o.OverrideDate, o.EqATarget)).ToList();
    }

    [HttpPost("{id:int}/capacity-overrides")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CapacityOverrideDto>> CreateCapacityOverride(int id, UpsertCapacityOverrideRequest request)
    {
        if (!await _db.Stations.AnyAsync(s => s.Id == id))
        {
            return NotFound();
        }

        var entity = new StationCapacityOverride
        {
            StationId = id,
            OverrideDate = request.OverrideDate,
            EqATarget = request.EqATarget,
            CreatedAt = SiteClock.Now
        };
        _db.StationCapacityOverrides.Add(entity);
        await _db.SaveChangesAsync();
        return new CapacityOverrideDto(entity.Id, entity.StationId, entity.OverrideDate, entity.EqATarget);
    }

    [HttpPut("{id:int}/capacity-overrides/{overrideId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CapacityOverrideDto>> UpdateCapacityOverride(int id, int overrideId, UpsertCapacityOverrideRequest request)
    {
        var entity = await _db.StationCapacityOverrides.FirstOrDefaultAsync(o => o.Id == overrideId && o.StationId == id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.OverrideDate = request.OverrideDate;
        entity.EqATarget = request.EqATarget;
        await _db.SaveChangesAsync();
        return new CapacityOverrideDto(entity.Id, entity.StationId, entity.OverrideDate, entity.EqATarget);
    }

    [HttpDelete("{id:int}/capacity-overrides/{overrideId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCapacityOverride(int id, int overrideId)
    {
        var entity = await _db.StationCapacityOverrides.FirstOrDefaultAsync(o => o.Id == overrideId && o.StationId == id);
        if (entity is null)
        {
            return NotFound();
        }

        _db.StationCapacityOverrides.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
