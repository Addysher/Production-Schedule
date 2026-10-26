using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    // --- Batchcut watch paths (§5.12) ---

    [HttpGet("batchcut-watch-paths")]
    public async Task<ActionResult<List<BatchcutWatchPathDto>>> GetBatchcutWatchPaths()
    {
        var paths = await _db.BatchcutWatchPaths.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        return paths.Select(p => new BatchcutWatchPathDto(p.Id, p.Name, p.UncPath, p.IsActive)).ToList();
    }

    [HttpPost("batchcut-watch-paths")]
    public async Task<ActionResult<BatchcutWatchPathDto>> CreateBatchcutWatchPath(UpsertBatchcutWatchPathRequest request)
    {
        var path = new BatchcutWatchPath { Name = request.Name, UncPath = request.UncPath, IsActive = request.IsActive };
        _db.BatchcutWatchPaths.Add(path);
        await _db.SaveChangesAsync();
        return new BatchcutWatchPathDto(path.Id, path.Name, path.UncPath, path.IsActive);
    }

    [HttpPut("batchcut-watch-paths/{id:int}")]
    public async Task<ActionResult<BatchcutWatchPathDto>> UpdateBatchcutWatchPath(int id, UpsertBatchcutWatchPathRequest request)
    {
        var path = await _db.BatchcutWatchPaths.FindAsync(id);
        if (path is null)
        {
            return NotFound();
        }

        path.Name = request.Name;
        path.UncPath = request.UncPath;
        path.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return new BatchcutWatchPathDto(path.Id, path.Name, path.UncPath, path.IsActive);
    }

    [HttpDelete("batchcut-watch-paths/{id:int}")]
    public async Task<IActionResult> DeleteBatchcutWatchPath(int id)
    {
        var path = await _db.BatchcutWatchPaths.FindAsync(id);
        if (path is null)
        {
            return NotFound();
        }

        // A path with logged events can't be removed without losing its Batchcut Log history (and the
        // foreign key forbids it), so it is switched off instead: it stops being watched.
        if (await _db.BatchcutEvents.AnyAsync(e => e.BatchcutPathId == id))
        {
            path.IsActive = false;
            await _db.SaveChangesAsync();
            return Ok(new BatchcutWatchPathDto(path.Id, path.Name, path.UncPath, path.IsActive));
        }

        _db.BatchcutWatchPaths.Remove(path);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // --- Batchcut event log (§5.12 — "Admin users can view the error detail via Admin → Settings → Batchcut log") ---

    [HttpGet("batchcut-events")]
    public async Task<ActionResult<List<BatchcutEventDto>>> GetBatchcutEvents([FromQuery] int take = 50)
    {
        var events = await _db.BatchcutEvents
            .AsNoTracking()
            .OrderByDescending(e => e.DetectedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();

        return events.Select(e => new BatchcutEventDto(e.Id, e.BatchcutPathId, e.FolderName, e.JobRef, e.DetectedAt, e.Status, e.ErrorMessage)).ToList();
    }
}
