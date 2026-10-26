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

    // Purely time-based per §8.3: the line item's EqA at the speed of the product's first line (its EqA per
    // hour, v3.0), in the first gap on that line. Setup times and operators are out of scope.
    [HttpPost("estimate")]
    public async Task<ActionResult<QuoteEstimateResponse>> Estimate(QuoteEstimateRequest request)
    {
        if (request.Lines.Count == 0)
        {
            return BadRequest("At least one line item is required.");
        }

        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        var now = SiteClock.Now;
        var results = new List<QuoteLineResult>();
        var busyByLine = new Dictionary<int, List<(DateTime Start, DateTime End)>>();

        foreach (var line in request.Lines)
        {
            var jobType = await _db.JobTypes.AsNoTracking().FirstOrDefaultAsync(jt => jt.Id == line.JobTypeId);
            if (jobType is null)
            {
                return BadRequest($"Job type {line.JobTypeId} does not exist.");
            }

            // v3.0: the first gap long enough on the product's first line (the scheduler's own rule), using
            // that line's speed and its working days (incl. overtime / closures). Earlier line items in this
            // quote take their place first.
            var station = await _db.Stations.AsNoTracking().Where(st => st.IsActive && st.ComponentType == jobType.ComponentType)
                .OrderBy(st => st.DisplayOrder).ThenBy(st => st.Id).FirstOrDefaultAsync();
            if (station is null)
            {
                return BadRequest($"No active line makes '{jobType.ComponentType}', so '{jobType.Name}' can't be estimated.");
            }

            if (!busyByLine.TryGetValue(station.Id, out var busy))
            {
                busy = (await _db.ScheduleItems.AsNoTracking()
                        .Where(i => i.StationId == station.Id && !i.IsArchived && i.ActualEnd == null)
                        .Select(i => new { i.PlannedStart, i.PlannedEnd, i.ActualStart, i.Job.EstimatedCompletionDate })
                        .ToListAsync())
                    .Select(i => i.ActualStart is null ? (i.PlannedStart, i.PlannedEnd) : (now, i.EstimatedCompletionDate ?? i.PlannedEnd))
                    .ToList();
                busyByLine[station.Id] = busy;
            }

            var changes = await _db.StationCapacityOverrides.AsNoTracking().Where(o => o.StationId == station.Id).ToDictionaryAsync(o => o.OverrideDate);
            var windowFor = calendar.WindowFor(changes);
            var durationMinutes = (double)line.Qty * 60 / (station.EqaPerHour is > 0 ? (double)station.EqaPerHour.Value : 60);
            var from = WorkingCalendar.AdvanceToWorkingWindow(now, windowFor, calendar.Breaks);
            DateTime completion;
            while (true)
            {
                completion = WorkingCalendar.PlaceDuration(from, durationMinutes, windowFor, calendar.Breaks);
                var start = from;
                var clash = busy.Where(x => x.Start < completion && x.End > start).OrderBy(x => x.End).LastOrDefault();
                if (clash == default)
                {
                    break;
                }

                from = WorkingCalendar.AdvanceToWorkingWindow(clash.End, windowFor, calendar.Breaks);
            }

            busy.Add((from, completion));

            results.Add(new QuoteLineResult(jobType.Id, jobType.Name, line.Qty, completion));
        }

        var fullOrderReadyAt = results.Max(r => r.EstimatedCompletion);
        return new QuoteEstimateResponse(request.Ref, results, fullOrderReadyAt);
    }
}
