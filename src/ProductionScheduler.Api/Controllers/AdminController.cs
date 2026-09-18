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

    // --- SCH import paths (§5.16) ---

    [HttpGet("sch-import-paths")]
    public async Task<ActionResult<List<SchImportPathDto>>> GetSchImportPaths()
    {
        var paths = await _db.SchImportPaths.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        return paths.Select(p => new SchImportPathDto(p.Id, p.Name, p.UncPath, p.ComponentType, p.IsActive)).ToList();
    }

    [HttpPost("sch-import-paths")]
    public async Task<ActionResult<SchImportPathDto>> CreateSchImportPath(UpsertSchImportPathRequest request)
    {
        var path = new SchImportPath
        {
            Name = request.Name,
            UncPath = request.UncPath,
            ComponentType = request.ComponentType,
            IsActive = request.IsActive
        };
        _db.SchImportPaths.Add(path);
        await _db.SaveChangesAsync();
        return new SchImportPathDto(path.Id, path.Name, path.UncPath, path.ComponentType, path.IsActive);
    }

    [HttpPut("sch-import-paths/{id:int}")]
    public async Task<ActionResult<SchImportPathDto>> UpdateSchImportPath(int id, UpsertSchImportPathRequest request)
    {
        var path = await _db.SchImportPaths.FindAsync(id);
        if (path is null)
        {
            return NotFound();
        }

        path.Name = request.Name;
        path.UncPath = request.UncPath;
        path.ComponentType = request.ComponentType;
        path.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return new SchImportPathDto(path.Id, path.Name, path.UncPath, path.ComponentType, path.IsActive);
    }

    [HttpDelete("sch-import-paths/{id:int}")]
    public async Task<IActionResult> DeleteSchImportPath(int id)
    {
        var path = await _db.SchImportPaths.FindAsync(id);
        if (path is null)
        {
            return NotFound();
        }

        _db.SchImportPaths.Remove(path);
        await _db.SaveChangesAsync();
        return NoContent();
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

        _db.BatchcutWatchPaths.Remove(path);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
