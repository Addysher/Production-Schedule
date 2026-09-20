using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ReportsController(AppDbContext db)
    {
        _db = db;
    }

    // Daily production value by station (§5.6). Built from when each member was actually completed, not
    // from the schedule: a job that runs over three days would otherwise put all of its EqA on day one.
    [HttpGet("production")]
    public async Task<ActionResult<List<ProductionRowDto>>> Production([FromQuery] DateOnly dateFrom, [FromQuery] DateOnly dateTo)
    {
        var rangeStart = dateFrom.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);

        var completions = await (
                from m in _db.JobMembers
                join j in _db.Jobs on m.JobRef equals j.JobRef
                where m.Done && m.CompletedAt != null && m.CompletedAt >= rangeStart && m.CompletedAt < rangeEnd && j.AssignedStationId != null
                select new { StationId = j.AssignedStationId!.Value, m.CompletedAt, Eqa = m.CompletedEqA ?? m.EqaValue })
            .ToListAsync();

        var stationNames = await _db.Stations.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name);

        return completions
            .GroupBy(c => (c.StationId, Date: DateOnly.FromDateTime(c.CompletedAt!.Value)))
            .Select(g => new ProductionRowDto(g.Key.StationId, stationNames.GetValueOrDefault(g.Key.StationId, "?"), g.Key.Date, g.Sum(c => c.Eqa)))
            .OrderBy(r => r.Date).ThenBy(r => r.StationName)
            .ToList();
    }
}
