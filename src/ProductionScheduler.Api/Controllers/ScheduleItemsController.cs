using ProductionScheduler.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/schedule-items")]
[Authorize(Roles = "Admin")]
public class ScheduleItemsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ScheduleItemsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost("{id:int}/approve-creep")]
    public async Task<IActionResult> ApproveCreep(int id)
    {
        var item = await _db.ScheduleItems.FindAsync(id);
        if (item is null)
        {
            return NotFound();
        }

        if (!item.CreepDetected)
        {
            return BadRequest("This schedule item has no unresolved creep to approve.");
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        item.CreepApprovedAt = SiteClock.Now;
        item.CreepApprovedByUserId = userId;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
