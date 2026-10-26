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
            .Include(si => si.Segments)
            .AsNoTracking()
            .ToListAsync();

        var frozen = await FreezeWindow.FrozenJobRefsAsync(_db);
        return items.Select(si => ToDto(si, frozen.Contains(si.JobRef) && si.ActualStart == null)).ToList();
    }

    // Readable by every role: the Calendar (Viewer-visible) needs the working window and breaks, but /api/settings is Admin-only.
    [HttpGet("calendar-settings")]
    public async Task<ActionResult<CalendarSettingsDto>> GetCalendarSettings()
    {
        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        return new CalendarSettingsDto(
            calendar.ShiftStartHour,
            calendar.ShiftEndHour,
            calendar.WorkingDays.OrderBy(d => d).ToList(),
            calendar.Breaks.Select(b => new BreakDto(b.Start.ToString(@"hh\:mm"), b.End.ToString(@"hh\:mm"))).ToList(),
            calendar.FreezeUntil(Domain.SiteClock.Now));
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

    private static ScheduleItemDto ToDto(Domain.Entities.ScheduleItem si, bool isFrozen) => new(
        si.Id, si.JobRef, si.Job.ClientName, si.StationId, si.Station.Name, si.Job.ComponentType,
        si.ScheduleDate, si.PlannedStart, si.PlannedEnd, si.PlannedEqA, si.MembersTotal,
        si.ActualStart, si.ActualEnd, si.EqaCompleted, si.MembersCompleted,
        si.CreepDetected, si.CreepApprovedAt, si.Job.Status,
        // Plans made before segments existed have none: treat the whole plan as one stretch on its start day.
        si.Segments.Count > 0
            ? si.Segments.OrderBy(s => s.SegmentStart).Select(s => new ScheduleSegmentDto(s.SegmentDate, s.SegmentStart, s.SegmentEnd, s.PlannedEqA)).ToList()
            : new List<ScheduleSegmentDto> { new(si.ScheduleDate, si.PlannedStart, si.PlannedEnd, si.PlannedEqA) },
        isFrozen);

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
