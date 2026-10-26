using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/quotes")]
public class QuoteController : ControllerBase
{
    private readonly AppDbContext _db;

    public QuoteController(AppDbContext db)
    {
        _db = db;
    }

    // Purely time-based per §8.3: after the product's current queue, the line item's EqA at the speed of the
    // product's first line (its EqA per hour, v3.0). Setup times and operators are out of scope.
    [HttpPost("estimate")]
    public async Task<ActionResult<QuoteEstimateResponse>> Estimate(QuoteEstimateRequest request)
    {
        if (request.Lines.Count == 0)
        {
            return BadRequest("At least one line item is required.");
        }

        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        var shiftStartHour = calendar.DayStart;
        var shiftEndHour = calendar.DayEnd;
        var workingDays = calendar.WorkingDays;
        var now = SiteClock.Now;
        var results = new List<QuoteLineResult>();

        foreach (var line in request.Lines)
        {
            var jobType = await _db.JobTypes.AsNoTracking().FirstOrDefaultAsync(jt => jt.Id == line.JobTypeId);
            if (jobType is null)
            {
                return BadRequest($"Job type {line.JobTypeId} does not exist.");
            }

            // Queue tail = the latest projected completion among active jobs already queued
            // at any station serving this job type's component group.
            var maxCompletion = await _db.Jobs
                .Where(j => !j.IsArchived && j.Status != "completed"
                    && j.ComponentType == jobType.ComponentType
                    && j.EstimatedCompletionDate != null)
                .MaxAsync(j => (DateTime?)j.EstimatedCompletionDate);

            var queueTail = maxCompletion is DateTime tail && tail > now ? tail : now;

            var eqaPerHour = await _db.Stations.Where(s => s.IsActive && s.ComponentType == jobType.ComponentType)
                .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Id).Select(s => s.EqaPerHour).FirstOrDefaultAsync();
            var durationMinutes = (double)line.Qty * 60 / (eqaPerHour is > 0 ? (double)eqaPerHour.Value : 60);
            var completion = WorkingCalendar.PlaceDuration(queueTail, durationMinutes, shiftStartHour, shiftEndHour, workingDays, calendar.Breaks);

            results.Add(new QuoteLineResult(jobType.Id, jobType.Name, line.Qty, completion));
        }

        var fullOrderReadyAt = results.Max(r => r.EstimatedCompletion);
        return new QuoteEstimateResponse(request.Ref, results, fullOrderReadyAt);
    }
}
