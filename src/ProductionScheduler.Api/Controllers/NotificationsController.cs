using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

// §5.1 / §5.12: the top-bar badge, "[2 new jobs · 1 error]". Counts what has happened since the caller
// last looked. The caller keeps its own "last seen" marker (the ServerNow from its previous response), so
// the server holds no per-user state and there is no client/server clock to reconcile.
[ApiController]
[Route("api/notifications")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<NotificationSummaryDto>> Get([FromQuery] DateTime? since)
    {
        var now = SiteClock.Now;
        if (since is null)
        {
            return new NotificationSummaryDto(0, 0, now); // first look: start counting from now
        }

        var newJobs = await _db.Jobs.CountAsync(j => j.CreatedAt > since);

        // Anything that went wrong unattended: a rejected SCH file, an unreadable PRD line, a failed
        // nightly task, a DONE folder the batchcut watcher couldn't place… — all written to
        // MaintenanceLogs, which is also what the Dashboard's 'Needs attention' panel lists.
        var errors = await _db.MaintenanceLogs.CountAsync(l => l.Outcome == "error" && l.RunAt > since);

        return new NotificationSummaryDto(newJobs, errors, now);
    }
}
