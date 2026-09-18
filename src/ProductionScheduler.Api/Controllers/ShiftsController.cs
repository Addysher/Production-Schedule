using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/shifts")]
[Authorize(Roles = "Admin")]
public class ShiftsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ShiftsController(AppDbContext db)
    {
        _db = db;
    }

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
        var shift = new Shift
        {
            Name = request.Name,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsActive = request.IsActive,
            Breaks = request.Breaks.Select(b => new ShiftBreak { BreakStart = b.BreakStart, BreakDurationMins = b.BreakDurationMins }).ToList()
        };
        _db.Shifts.Add(shift);
        await _db.SaveChangesAsync();
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

        shift.Name = request.Name;
        shift.StartTime = request.StartTime;
        shift.EndTime = request.EndTime;
        shift.IsActive = request.IsActive;

        _db.ShiftBreaks.RemoveRange(shift.Breaks);
        shift.Breaks = request.Breaks.Select(b => new ShiftBreak { BreakStart = b.BreakStart, BreakDurationMins = b.BreakDurationMins }).ToList();

        await _db.SaveChangesAsync();
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
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
