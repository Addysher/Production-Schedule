using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;
using OperatorEntity = ProductionScheduler.Domain.Entities.Operator;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/operators")]
[Authorize(Roles = "Admin")]
public class OperatorsController : ControllerBase
{
    private readonly AppDbContext _db;

    public OperatorsController(AppDbContext db)
    {
        _db = db;
    }

    private static OperatorDto ToDto(OperatorEntity o) => new(o.Id, o.Name, o.ShiftId, o.IsActive);

    [HttpGet]
    public async Task<ActionResult<List<OperatorDto>>> GetAll()
    {
        var operators = await _db.Operators.AsNoTracking().OrderBy(o => o.Name).ToListAsync();
        return operators.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<OperatorDto>> GetById(int id)
    {
        var op = await _db.Operators.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        return op is null ? NotFound() : ToDto(op);
    }

    [HttpPost]
    public async Task<ActionResult<OperatorDto>> Create(UpsertOperatorRequest request)
    {
        var op = new OperatorEntity { Name = request.Name, ShiftId = request.ShiftId, IsActive = request.IsActive };
        _db.Operators.Add(op);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetById), new { id = op.Id }, ToDto(op));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<OperatorDto>> Update(int id, UpsertOperatorRequest request)
    {
        var op = await _db.Operators.FindAsync(id);
        if (op is null)
        {
            return NotFound();
        }

        op.Name = request.Name;
        op.ShiftId = request.ShiftId;
        op.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        return ToDto(op);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var op = await _db.Operators.FindAsync(id);
        if (op is null)
        {
            return NotFound();
        }

        op.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
