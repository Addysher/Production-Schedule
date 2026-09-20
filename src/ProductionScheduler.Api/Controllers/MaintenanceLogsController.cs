using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

// Read-only feed for the Dashboard/Jobs side panels (§5.15 — "Recent maintenance log entries",
// not Admin-gated like the batchcut log in §5.12, since the Dashboard itself is Viewer-visible.
[ApiController]
[Route("api/maintenance-logs")]
public class MaintenanceLogsController : ControllerBase
{
    private readonly AppDbContext _db;

    public MaintenanceLogsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<MaintenanceLogDto>>> GetRecent([FromQuery] int take = 5)
    {
        var logs = await _db.MaintenanceLogs
            .AsNoTracking()
            .OrderByDescending(l => l.RunAt)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync();

        return logs.Select(l => new MaintenanceLogDto(l.Id, l.TaskName, l.JobRef, l.SourcePath, l.DestPath, l.Outcome, l.Message, l.RunAt)).ToList();
    }
}
