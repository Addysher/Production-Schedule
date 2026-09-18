using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/schedule")]
public class ScheduleController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScheduleGenerationService _scheduleGenerationService;

    public ScheduleController(AppDbContext db, IScheduleGenerationService scheduleGenerationService)
    {
        _db = db;
        _scheduleGenerationService = scheduleGenerationService;
    }

    [HttpGet("current")]
    public async Task<ActionResult<List<ScheduleItemDto>>> GetCurrent()
    {
        var items = await _db.ScheduleItems
            .Where(si => !si.IsArchived)
            .Include(si => si.Station)
            .Include(si => si.Job)
            .AsNoTracking()
            .ToListAsync();

        return items.Select(ToDto).ToList();
    }

    [HttpPost("generate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ScheduleGenerationResult>> Generate()
    {
        return await _scheduleGenerationService.GenerateAsync();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] int? stationId, [FromQuery] DateOnly? dateFrom, [FromQuery] DateOnly? dateTo)
    {
        var query = _db.ScheduleItems.Where(si => !si.IsArchived).Include(si => si.Station).Include(si => si.Job).AsNoTracking();

        if (stationId is int sid)
        {
            query = query.Where(si => si.StationId == sid);
        }

        if (dateFrom is DateOnly from)
        {
            query = query.Where(si => si.ScheduleDate >= from);
        }

        if (dateTo is DateOnly to)
        {
            query = query.Where(si => si.ScheduleDate <= to);
        }

        var items = await query.OrderBy(si => si.ScheduleDate).ThenBy(si => si.StationId).ToListAsync();

        var csv = new StringBuilder();
        csv.AppendLine("JobRef,ClientName,Station,ComponentType,ScheduleDate,PlannedStart,PlannedEnd,PlannedEqA,ActualStart,ActualEnd,EqaCompleted,Status");
        foreach (var si in items)
        {
            csv.AppendLine(string.Join(',', new[]
            {
                CsvEscape(si.JobRef),
                CsvEscape(si.Job.ClientName),
                CsvEscape(si.Station.Name),
                CsvEscape(si.Job.ComponentType),
                si.ScheduleDate.ToString("yyyy-MM-dd"),
                si.PlannedStart.ToString("O"),
                si.PlannedEnd.ToString("O"),
                si.PlannedEqA.ToString(),
                si.ActualStart?.ToString("O") ?? "",
                si.ActualEnd?.ToString("O") ?? "",
                si.EqaCompleted.ToString(),
                CsvEscape(si.Job.Status)
            }));
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv", "schedule-export.csv");
    }

    private static ScheduleItemDto ToDto(Domain.Entities.ScheduleItem si) => new(
        si.Id, si.JobRef, si.Job.ClientName, si.StationId, si.Station.Name, si.Job.ComponentType,
        si.ScheduleDate, si.PlannedStart, si.PlannedEnd, si.PlannedEqA, si.MembersTotal,
        si.ActualStart, si.ActualEnd, si.EqaCompleted, si.MembersCompleted,
        si.CreepDetected, si.CreepApprovedAt, si.Job.Status);

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
