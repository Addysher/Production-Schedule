using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

public record FactoryDto(int Id, string Name, bool IsActive, List<int> StationIds);

public record UpsertFactoryRequest(string? Name, bool IsActive);

// v3.0 — factories group stations for the kiosk display (Settings → Factories; a station's factory is
// set on the station). Scheduling ignores them: a job can go to any station that makes its product.
[ApiController]
[Route("api/factories")]
public class FactoriesController : ControllerBase
{
    private readonly AppDbContext _db;

    public FactoriesController(AppDbContext db)
    {
        _db = db;
    }

    private static FactoryDto ToDto(Factory f) =>
        new(f.Id, f.Name, f.IsActive, f.Stations.OrderBy(s => s.DisplayOrder).Select(s => s.Id).ToList());

    // Readable by every role: the kiosk picker lists them.
    [HttpGet]
    public async Task<ActionResult<List<FactoryDto>>> GetAll() =>
        (await _db.Factories.AsNoTracking().Include(f => f.Stations).OrderBy(f => f.Name).ToListAsync()).Select(ToDto).ToList();

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<FactoryDto>> Create(UpsertFactoryRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Factory name is required.");
        }

        if (await _db.Factories.AnyAsync(f => f.Name == name))
        {
            return Conflict($"A factory called '{name}' already exists.");
        }

        var factory = new Factory { Name = name, IsActive = request.IsActive };
        _db.Factories.Add(factory);
        await _db.SaveChangesAsync();
        return ToDto(factory);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<FactoryDto>> Update(int id, UpsertFactoryRequest request)
    {
        var factory = await _db.Factories.Include(f => f.Stations).FirstOrDefaultAsync(f => f.Id == id);
        if (factory is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Factory name is required.");
        }

        if (await _db.Factories.AnyAsync(f => f.Id != id && f.Name == name))
        {
            return Conflict($"A factory called '{name}' already exists.");
        }

        factory.Name = name;
        factory.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return ToDto(factory);
    }

    // Its stations stay, unassigned.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var factory = await _db.Factories.Include(f => f.Stations).FirstOrDefaultAsync(f => f.Id == id);
        if (factory is null)
        {
            return NotFound();
        }

        foreach (var station in factory.Stations)
        {
            station.FactoryId = null;
        }

        _db.Factories.Remove(factory);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
