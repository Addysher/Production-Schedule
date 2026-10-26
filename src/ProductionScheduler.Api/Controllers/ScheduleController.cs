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

    // Dashboard station cards (§7): for each job running now, the member being made and the one after it,
    // in the design's batch/sequence order.
    // Is the floor keeping up? For each active line today: the Eq planned to be made by now (today's plan
    // segments up to now, part of the current one pro rata) against what the PRD files say was made today.
    [HttpGet("line-progress")]
    public async Task<ActionResult<List<LineProgressDto>>> LineProgress()
    {
        var now = Domain.SiteClock.Now;
        var today = DateOnly.FromDateTime(now);
        var start = today.ToDateTime(TimeOnly.MinValue);
        var segments = await _db.ScheduleSegments.AsNoTracking()
            .Where(s => s.SegmentDate == today && !s.ScheduleItem.IsArchived)
            .Select(s => new { s.ScheduleItem.StationId, s.SegmentStart, s.SegmentEnd, s.PlannedEqA })
            .ToListAsync();
        var made = await (
                from m in _db.JobMembers
                join j in _db.Jobs on m.JobRef equals j.JobRef
                where m.Done && m.CompletedAt >= start && m.CompletedAt < start.AddDays(1) && j.AssignedStationId != null
                group (m.CompletedEqA ?? m.EqaValue) by j.AssignedStationId!.Value into g
                select new { StationId = g.Key, Eqa = g.Sum() })
            .ToDictionaryAsync(r => r.StationId, r => r.Eqa);
        var stationIds = await _db.Stations.Where(s => s.IsActive).Select(s => s.Id).ToListAsync();

        return stationIds.Select(id =>
        {
            var mine = segments.Where(s => s.StationId == id).ToList();
            var byNow = mine.Sum(s => s.SegmentEnd <= now ? s.PlannedEqA
                : s.SegmentStart >= now ? 0
                : s.PlannedEqA * (decimal)((now - s.SegmentStart).TotalMinutes / (s.SegmentEnd - s.SegmentStart).TotalMinutes));
            return new LineProgressDto(id, Math.Round(byNow, 1), Math.Round(made.GetValueOrDefault(id), 1), Math.Round(mine.Sum(s => s.PlannedEqA), 1));
        }).ToList();
    }

    [HttpGet("station-progress")]
    public async Task<ActionResult<List<StationProgressDto>>> StationProgress()
    {
        var running = await _db.ScheduleItems
            .Where(si => !si.IsArchived && si.ActualStart != null && si.ActualEnd == null)
            .Select(si => new { si.StationId, si.JobRef })
            .ToListAsync();
        var jobRefs = running.Select(r => r.JobRef).Distinct().ToList();
        var members = await _db.JobMembers.AsNoTracking()
            .Where(m => jobRefs.Contains(m.JobRef))
            .OrderBy(m => m.Batch).ThenBy(m => m.Sequence).ThenBy(m => m.SubSequence).ThenBy(m => m.MemberId)
            .Select(m => new { m.JobRef, m.MemberId, m.Done })
            .ToListAsync();

        return running.Select(r =>
            {
                var ofJob = members.Where(m => m.JobRef == r.JobRef).ToList();
                var toDo = ofJob.Where(m => !m.Done).Select(m => m.MemberId).ToList();
                return new StationProgressDto(r.StationId, r.JobRef, toDo.ElementAtOrDefault(0), toDo.ElementAtOrDefault(1), ofJob.Count(m => m.Done), ofJob.Count);
            })
            .ToList();
    }

    // Readable by every role: the Calendar (Viewer-visible) needs the working window and breaks, but /api/settings is Admin-only.
    [HttpGet("calendar-settings")]
    public async Task<ActionResult<CalendarSettingsDto>> GetCalendarSettings()
    {
        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        return new CalendarSettingsDto(
            (int)Math.Floor(calendar.DayStart.TotalHours),
            (int)Math.Ceiling(calendar.DayEnd.TotalHours),
            calendar.WorkingDays.OrderBy(d => d).ToList(),
            calendar.Breaks.Select(b => new BreakDto(Clock(b.Start), Clock(b.End))).ToList(), // a break to midnight ends "24:00"
            calendar.FreezeUntil(Domain.SiteClock.Now),
            Clock(calendar.DayStart),
            Clock(calendar.DayEnd));

        // "24:00" for a day that runs to midnight (TimeSpan's own format would wrap it to 00:00).
        static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";
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
        isFrozen,
        si.Job.BookedDeliveryDate is null); // TBA: pencilled in until booked

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
