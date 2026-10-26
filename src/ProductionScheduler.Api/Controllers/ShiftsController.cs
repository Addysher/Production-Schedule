using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize(Roles = "Admin")]
public class ShiftsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScheduleGenerationService _scheduleGenerationService;

    public ShiftsController(AppDbContext db, IScheduleGenerationService scheduleGenerationService)
    {
        _db = db;
        _scheduleGenerationService = scheduleGenerationService;
    }

    // The active shifts set the working day (start to end) and its breaks, so a shift has to make sense on
    // its own: it starts before it ends (00:00 as an end means midnight; overnight shifts aren't supported)
    // and every break sits inside it without overlapping another.
    private static string? Problem(UpsertShiftRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Give the shift a name.";
        }

        var start = request.StartTime.ToTimeSpan();
        var end = request.EndTime == TimeOnly.MinValue ? TimeSpan.FromHours(24) : request.EndTime.ToTimeSpan();
        if (end <= start)
        {
            return $"The shift must end after it starts ({request.StartTime:HH:mm} to {request.EndTime:HH:mm}). Shifts that run past midnight aren't supported.";
        }

        TimeSpan? previousEnd = null;
        foreach (var b in request.Breaks.OrderBy(b => b.BreakStart))
        {
            var breakStart = b.BreakStart.ToTimeSpan();
            var breakEnd = breakStart + TimeSpan.FromMinutes(b.BreakDurationMins);
            if (b.BreakDurationMins <= 0)
            {
                return $"The break at {b.BreakStart:HH:mm} needs a length in minutes.";
            }

            if (breakStart < start || breakEnd > end)
            {
                return $"The break at {b.BreakStart:HH:mm} ({b.BreakDurationMins} min) is outside the shift ({request.StartTime:HH:mm} to {request.EndTime:HH:mm}).";
            }

            if (previousEnd is { } pe && breakStart < pe)
            {
                return $"The break at {b.BreakStart:HH:mm} overlaps the break before it.";
            }

            previousEnd = breakEnd;
        }

        if (request.Breaks.Sum(b => b.BreakDurationMins) >= (end - start).TotalMinutes)
        {
            return "The breaks fill the whole shift, so it has no working time.";
        }

        return null;
    }

    // Saves, then re-plans in the same transaction: if the new calendar leaves no working time at all
    // (e.g. breaks from several shifts that together cover the day), the save is undone with the reason
    // instead of leaving a calendar every later re-plan would fail on.
    private Task<string?> SaveAndReplanAsync() => _scheduleGenerationService.SaveAndReplanAsync();

    private static ShiftDto ToDto(Shift s) => new(
        s.Id, s.Name, s.StartTime, s.EndTime, s.IsActive,
        s.Breaks.Select(b => new ShiftBreakDto(b.Id, b.BreakStart, b.BreakDurationMins)).ToList());

    [HttpGet]
    public async Task<ActionResult<List<ShiftDto>>> GetAll()
    {
        var shifts = await _db.Shifts.Include(s => s.Breaks).AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        return shifts.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShiftDto>> GetById(int id)
    {
        var shift = await _db.Shifts.Include(s => s.Breaks).AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return shift is null ? NotFound() : ToDto(shift);
    }

    [HttpPost]
    public async Task<ActionResult<ShiftDto>> Create(UpsertShiftRequest request)
    {
        if (Problem(request) is { } problem)
        {
            return BadRequest(problem);
        }

        var shift = new Shift
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = request.IsActive,
            Breaks = request.Breaks.Select(b => new ShiftBreak { BreakStart = b.BreakStart, BreakDurationMins = b.BreakDurationMins }).ToList()
        };
        _db.Shifts.Add(shift);
        if (await SaveAndReplanAsync() is { } noWorkingTime) // the working day may have changed
        {
            return BadRequest(noWorkingTime);
        }

        return CreatedAtAction(nameof(GetById), new { id = shift.Id }, ToDto(shift));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ShiftDto>> Update(int id, UpsertShiftRequest request)
    {
        var shift = await _db.Shifts.Include(s => s.Breaks).FirstOrDefaultAsync(s => s.Id == id);
        if (shift is null)
        {
            return NotFound();
        }

        if (Problem(request) is { } problem)
        {
            return BadRequest(problem);
        }

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.IsActive = request.IsActive;

        _db.ShiftBreaks.RemoveRange(shift.Breaks);
        shift.Breaks = request.Breaks.Select(b => new ShiftBreak { BreakStart = b.BreakStart, BreakDurationMins = b.BreakDurationMins }).ToList();

        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return ToDto(shift);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var shift = await _db.Shifts.FindAsync(id);
        if (shift is null)
        {
            return NotFound();
        }

        shift.IsActive = false;
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return NoContent();
    }
}
