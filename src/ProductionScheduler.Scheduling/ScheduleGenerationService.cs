using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.ConstraintResolution;

namespace ProductionScheduler.Scheduling;

// JobsSkipped = queued jobs with no active line to run on (left unassigned rather than aborting the run).
// JobsFrozen = jobs kept in their slot because they start inside the freeze window.
// JobsPencilled = TBA jobs (no booked delivery date) planned as pencilled-in work.
public record ScheduleGenerationResult(int JobsScheduled, int ScheduleItemsWritten, int JobsSkipped = 0, int JobsFrozen = 0, int JobsPencilled = 0);

public interface IScheduleGenerationService
{
    Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default);

    // Saves the caller's pending changes and re-plans, as one transaction and while holding the run lock
    // (so a save never waits on a running re-plan with its rows locked). If the result leaves no working
    // time at all, everything is rolled back and the reason returned.
    Task<string?> SaveAndReplanAsync(CancellationToken ct = default);
}

// v3.0 scheduler, following how the factory plans by hand (§3.3):
//
// 1. What gets planned. A job is CALLED UP when the office has entered its Booked Delivery date: that date
//    is its deadline and its priority. A job without one (TBA, including quotes) is PENCILLED IN by its
//    rough date (Est. Booked, else Expected Delivery) when PencilTbaJobs is on: it holds its slot like any
//    job, but can't be approved for cutting or sent to a line until it's booked. With PencilTbaJobs off,
//    TBA jobs aren't planned at all.
// 2. When. Jobs are taken in deadline order (precedence rules on top). Each should finish at least
//    ProductionLeadDaysMin working days before delivery, and may not start more than ProductionLeadDaysMax
//    working days before it (0 = no limit), so finished work doesn't fill the yard. Cut jobs get no special
//    treatment: cutting follows the production order.
// 3. Where. One job per line, never split. The first line that makes the product (by display order) takes
//    the job if it can finish it in time; only otherwise does the work go to the next line. If no line can
//    make the date, the job goes where it finishes soonest (and shows as at risk / late).
// 4. How long. A line makes EqaPerHour EqA per working hour (not set: 60, i.e. a minute per EqA). Each line
//    works the shifts' day minus breaks, except on days with a one-off change for that line: overtime
//    hours, or closed (shutdown / public holiday). A job fills the first gap on its line that is long
//    enough, so earlier gaps (e.g. before a pinned job) get used.
//
// Work already running is never re-planned: it holds its line until its projected end (the work still to
// do placed from now — or, while creep is unapproved, its original plan end, §5.14) and is redrawn from
// now. Frozen work (§5.20) keeps its line, order and start but follows the current hours and breaks.
// Pinned jobs (Schedule manually / drag-and-drop) start on their pinned day, or as soon as their line is
// free after it.
public class ScheduleGenerationService : IScheduleGenerationService
{
    private const double UnsetEqaPerHour = 60;
    private static readonly string[] Plannable = ["ready", "approved", "cut"];

    private readonly AppDbContext _db;

    public ScheduleGenerationService(AppDbContext db)
    {
        _db = db;
    }

    // One run at a time across the whole app: a background import and a user's drag (each with its own
    // DbContext) would otherwise rewrite the same ScheduleItems concurrently and collide.
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public async Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default)
    {
        await RunLock.WaitAsync(ct);
        try
        {
            return await GenerateCoreAsync(ct);
        }
        finally
        {
            RunLock.Release();
        }
    }

    public async Task<string?> SaveAndReplanAsync(CancellationToken ct = default)
    {
        await RunLock.WaitAsync(ct);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            await _db.SaveChangesAsync(ct);
            try
            {
                await GenerateCoreAsync(ct);
            }
            catch (InvalidOperationException ex)
            {
                await transaction.RollbackAsync(ct);
                _db.ChangeTracker.Clear();
                return ex.Message;
            }

            await transaction.CommitAsync(ct);
            return null;
        }
        finally
        {
            RunLock.Release();
        }
    }

    // A line's view of time: its own working hours per day, and what it makes per hour.
    private sealed record Line(Station Station, Func<DateOnly, DayWindow?> WindowFor, double MinsPerEqa)
    {
        public int Id => Station.Id;

        // Busy stretches already taken on this line (running, frozen, pinned and placed work).
        public List<(DateTime Start, DateTime End)> Busy { get; } = [];

        // The end of this line's frozen work: nothing else is planned before it (§5.20, nothing is dropped
        // into the freeze window), even where a gap opens up.
        public DateTime FrozenUntil { get; set; } = DateTime.MinValue;
    }

    private async Task<ScheduleGenerationResult> GenerateCoreAsync(CancellationToken ct)
    {
        var stations = await _db.Stations.Where(s => s.IsActive).AsNoTracking().OrderBy(s => s.DisplayOrder).ThenBy(s => s.Id).ToListAsync(ct);
        if (stations.Count == 0)
        {
            return new ScheduleGenerationResult(0, 0);
        }

        var calendar = await WorkCalendarSettings.LoadAsync(_db, ct);
        var breaks = calendar.Breaks;
        var generatedAt = SiteClock.Now;

        var dayChanges = (await _db.StationCapacityOverrides.AsNoTracking().ToListAsync(ct))
            .GroupBy(o => o.StationId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<DateOnly, StationCapacityOverride>)g.ToDictionary(o => o.OverrideDate));
        var lines = stations.ToDictionary(s => s.Id, s => new Line(
            s,
            calendar.WindowFor(dayChanges.GetValueOrDefault(s.Id)),
            60 / (s.EqaPerHour is > 0 ? (double)s.EqaPerHour.Value : UnsetEqaPerHour)));

        var unapprovedCreepEnds = (await _db.ScheduleItems
                .Where(si => !si.IsArchived && si.CreepDetected && si.CreepApprovedAt == null)
                .Select(si => new { si.JobRef, si.PlannedEnd })
                .ToListAsync(ct))
            .GroupBy(x => x.JobRef)
            .ToDictionary(g => g.Key, g => g.Max(x => x.PlannedEnd));

        // Called-up (booked) jobs, and TBA ones (no booked date, including quotes) that may be pencilled in.
        var candidates = (await _db.Jobs
                .Where(j => !j.IsArchived && j.ComponentType != null
                    && ((j.DesignStatus == "FinalDesign" && Plannable.Contains(j.Status)) || (j.DesignStatus == "Quote" && j.Status == "scheduled")))
                .ToListAsync(ct))
            .Where(j => !unapprovedCreepEnds.ContainsKey(j.JobRef))
            .ToList();
        // Booked = the office has entered a Booked Delivery date (quote or final design): it gets a firm place.
        static bool IsBooked(Job j) => j.BookedDeliveryDate is not null;
        var notPencilled = calendar.PencilTbaJobs ? [] : candidates.Where(j => !IsBooked(j)).ToList();
        var queued = candidates.Except(notPencilled).ToList();

        var inFlight = await _db.Jobs
            .Where(j => !j.IsArchived && j.Status == "in-production" && j.AssignedStationId != null)
            .OrderBy(j => j.EstimatedStartDate)
            .ToListAsync(ct);

        // A booked job is due on its booked date; a TBA job on its rough date. A rough date that has already
        // passed has gone stale (estimates are pushed until the office books delivery), so it counts as unknown
        // — two weeks out, like a job with no date — and can't jump ahead of booked work that can be made.
        var today = DateOnly.FromDateTime(generatedAt);
        DateOnly DueOf(Job job)
        {
            if (IsBooked(job))
            {
                return job.BookedDeliveryDate!.Value;
            }

            var rough = job.ExpectedDeliveryDate ?? job.EstBookedDeliveryDate;
            return rough is { } r && r >= today ? r : today.AddDays(14);
        }

        // --- Work already on the floor: holds its line until its projected end, and is redrawn from now. ---
        var inFlightRefs = inFlight.Select(j => j.JobRef).ToList();
        var inFlightItems = await _db.ScheduleItems
            .Include(si => si.Segments)
            .Where(si => !si.IsArchived && si.ActualStart != null && si.ActualEnd == null && inFlightRefs.Contains(si.JobRef))
            .ToListAsync(ct);
        foreach (var job in inFlight)
        {
            if (!lines.TryGetValue(job.AssignedStationId!.Value, out var line))
            {
                continue;
            }

            var from = WorkingCalendar.AdvanceToWorkingWindow(generatedAt, line.WindowFor, breaks);
            var lineFree = line.Busy.Count > 0 ? line.Busy.Max(b => b.End) : from;
            if (lineFree > from)
            {
                from = lineFree;
            }

            DateTime end;
            if (unapprovedCreepEnds.TryGetValue(job.JobRef, out var creepEnd))
            {
                end = creepEnd > from ? creepEnd : from;
            }
            else
            {
                var remainingEqa = Math.Max((double)((job.TotalEqA ?? 0) - job.CompletedEqA), 0);
                var projected = new List<WorkSegment>();
                end = WorkingCalendar.PlaceWork(from, remainingEqa, line.MinsPerEqa, line.WindowFor, _ => null, [], breaks, projected);
                if (job.TotalEqA is > 0)
                {
                    job.EstimatedCompletionDate = end;
                    job.UpdatedAt = generatedAt;
                }

                // The Calendar draws segments, so a running job shows where its remaining work now falls. What's
                // already past stays, and so does the original plan (PlannedStart/PlannedEnd) for plan-vs-actual.
                if (inFlightItems.FirstOrDefault(i => i.JobRef == job.JobRef) is { } running)
                {
                    ReplaceSegmentsFrom(running, generatedAt, projected);
                }
            }

            line.Busy.Add((generatedAt, end));
        }

        // --- Frozen work: keeps its line, order and start; follows the current hours and breaks. ---
        // Always re-timed from where it was planned (FrozenAnchorStart), so a temporary push never sticks.
        var frozenPlans = (await FreezeWindow.FrozenPlansAsync(_db, calendar.FreezeUntil(generatedAt), ct))
            .Where(kv => queued.Any(j => j.JobRef == kv.Key && IsBooked(j) // TBA work isn't frozen: it can't be made yet
                && lines.TryGetValue(kv.Value[0].StationId, out var l) && l.Station.ComponentType == j.ComponentType))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var frozenJobs = queued.Where(j => frozenPlans.ContainsKey(j.JobRef)).ToDictionary(j => j.JobRef);
        queued = queued.Where(j => !frozenPlans.ContainsKey(j.JobRef)).ToList();

        var frozenRefs = frozenPlans.Keys.ToList();
        var frozenItems = await _db.ScheduleItems
            .Include(si => si.Segments)
            .Where(si => !si.IsArchived && si.ActualStart == null && frozenRefs.Contains(si.JobRef))
            .ToListAsync(ct);
        foreach (var lineItems in frozenItems.GroupBy(i => i.StationId).Where(g => lines.ContainsKey(g.Key)))
        {
            var line = lines[lineItems.Key];
            var freeAt = line.Busy.Count > 0 ? line.Busy.Max(b => b.End) : DateTime.MinValue;
            foreach (var item in lineItems.OrderBy(i => i.FrozenAnchorStart ?? i.PlannedStart))
            {
                var job = frozenJobs[item.JobRef];
                var anchor = item.FrozenAnchorStart ??= item.PlannedStart;
                var start = WorkingCalendar.AdvanceToWorkingWindow(anchor > freeAt ? anchor : freeAt, line.WindowFor, breaks);
                var placed = new List<WorkSegment>();
                var end = WorkingCalendar.PlaceWork(start, (double)item.PlannedEqA, line.MinsPerEqa, line.WindowFor, _ => null, [], breaks, placed);

                item.PlannedStart = placed.Count > 0 ? placed[0].Start : start;
                item.PlannedEnd = end;
                item.ScheduleDate = DateOnly.FromDateTime(item.PlannedStart);
                ReplaceSegmentsFrom(item, DateTime.MinValue, placed);
                job.EstimatedStartDate = item.PlannedStart;
                job.EstimatedCompletionDate = end;
                line.Busy.Add((item.PlannedStart, end));
                freeAt = end;
            }

            line.FrozenUntil = freeAt;
        }

        // --- Jobs that won't be planned this run lose any old (not-started) plan and show as unscheduled. ---
        // A pin only holds while its line is active and still makes the job's product.
        bool IsEffectivelyPinned(Job j) => j.IsManuallyScheduled && j.ManualStationId is int id
            && lines.TryGetValue(id, out var l) && l.Station.ComponentType == j.ComponentType;
        List<Line> LinesFor(Job j) => IsEffectivelyPinned(j)
            ? [lines[j.ManualStationId!.Value]]
            : lines.Values.Where(l => l.Station.ComponentType == j.ComponentType).ToList(); // already in display order

        var routable = queued.Where(j => LinesFor(j).Count > 0).ToList();
        var skipped = queued.Count - routable.Count;
        foreach (var job in queued.Where(j => LinesFor(j).Count == 0).Concat(notPencilled))
        {
            _db.ScheduleItems.RemoveRange(await _db.ScheduleItems.Where(si => si.JobRef == job.JobRef && si.ActualStart == null).ToListAsync(ct));
            job.AssignedStationId = null;
            job.EstimatedStartDate = null;
            job.EstimatedCompletionDate = null;
            job.UpdatedAt = generatedAt;
        }

        // --- Order: deadline first, precedence rules on top; pinned jobs first so their days are held. ---
        var byRef = routable.ToDictionary(j => j.JobRef);
        var allEdges = (await _db.Precedences.AsNoTracking().Select(p => new { p.PredecessorJobRef, p.SuccessorJobRef }).ToListAsync(ct))
            .Select(e => (e.PredecessorJobRef, e.SuccessorJobRef))
            .ToList();
        var edges = allEdges.Where(e => byRef.ContainsKey(e.PredecessorJobRef) && byRef.ContainsKey(e.SuccessorJobRef)).ToList();
        var ordered = edges.Count > 0
            ? PrecedenceGraph.TopologicalSort(byRef.Keys, edges, r => DueOf(byRef[r]).ToDateTime(TimeOnly.MinValue)).Select(r => byRef[r]).ToList()
            : routable.OrderBy(DueOf).ThenBy(j => j.BookedDeliveryDate is null).ThenBy(j => j.JobRef).ToList();
        var pinnedFirst = ordered.Where(IsEffectivelyPinned).OrderBy(j => j.ManualScheduleDate).Concat(ordered.Where(j => !IsEffectivelyPinned(j)));

        // Predecessors that are already running or frozen count too: a successor waits for them.
        var plannedEnds = inFlight.Where(j => j.EstimatedCompletionDate is not null).ToDictionary(j => j.JobRef, j => j.EstimatedCompletionDate!.Value);
        foreach (var frozen in frozenJobs.Values.Where(j => j.EstimatedCompletionDate is not null))
        {
            plannedEnds[frozen.JobRef] = frozen.EstimatedCompletionDate!.Value;
        }
        var scheduleItemsWritten = 0;
        foreach (var job in pinnedFirst)
        {
            var due = DueOf(job);
            var notBefore = generatedAt; // each line then moves on to its own next working moment (incl. overtime)
            var pinned = IsEffectivelyPinned(job) && job.ManualScheduleDate is not null;
            if (pinned && job.ManualScheduleDate!.Value.ToDateTime(TimeOnly.MinValue) > notBefore)
            {
                notBefore = job.ManualScheduleDate.Value.ToDateTime(TimeOnly.MinValue); // a pin is where someone put it
            }

            foreach (var predecessor in allEdges.Where(e => e.SuccessorJobRef == job.JobRef).Select(e => e.PredecessorJobRef))
            {
                if (plannedEnds.TryGetValue(predecessor, out var predecessorEnd) && predecessorEnd > notBefore)
                {
                    notBefore = predecessorEnd;
                }
            }

            // The first line that can finish it in time; otherwise wherever it finishes soonest.
            var eqa = (double)(job.TotalEqA ?? 0);
            (Line Line, DateTime Start, DateTime End)? chosen = null;
            foreach (var line in LinesFor(job))
            {
                // The delivery window counts this line's working days, so its closures (shutdowns, public
                // holidays) don't count as days the job could have been made.
                bool LineWorks(DateOnly d) => line.WindowFor(d) is not null;
                var latestFinish = calendar.LatestFinish(due, LineWorks);
                var from = notBefore > line.FrozenUntil ? notBefore : line.FrozenUntil;
                if (!pinned && calendar.EarliestStart(due, LineWorks) is { } earliest && earliest > from)
                {
                    from = earliest;
                }

                var (start, end) = FirstFit(line, eqa, from, breaks);
                if (end <= latestFinish)
                {
                    chosen = (line, start, end);
                    break;
                }

                if (chosen is null || end < chosen.Value.End)
                {
                    chosen = (line, start, end);
                }
            }

            var (target, plannedStart, _) = chosen!.Value;
            var segments = new List<WorkSegment>();
            var plannedEnd = WorkingCalendar.PlaceWork(plannedStart, eqa, target.MinsPerEqa, target.WindowFor, _ => null, [], breaks, segments);
            if (segments.Count > 0)
            {
                plannedStart = segments[0].Start;
            }

            target.Busy.Add((plannedStart, plannedEnd));
            plannedEnds[job.JobRef] = plannedEnd;

            // Replace any existing (not-yet-started) plan for this job — the schedule is being recalculated.
            _db.ScheduleItems.RemoveRange(await _db.ScheduleItems.Where(si => si.JobRef == job.JobRef && si.ActualStart == null).ToListAsync(ct));
            _db.ScheduleItems.Add(new ScheduleItem
            {
                JobRef = job.JobRef,
                StationId = target.Id,
                ScheduleDate = DateOnly.FromDateTime(plannedStart),
                PlannedStart = plannedStart,
                PlannedEnd = plannedEnd,
                PlannedEqA = job.TotalEqA ?? 0,
                MembersTotal = await _db.JobMembers.CountAsync(m => m.JobRef == job.JobRef, ct),
                Segments = segments
                    .Select(s => new ScheduleSegment { SegmentDate = s.Day, SegmentStart = s.Start, SegmentEnd = s.End, PlannedEqA = (decimal)s.Eqa })
                    .ToList(),
                GeneratedAt = generatedAt,
            });
            scheduleItemsWritten++;

            job.AssignedStationId = target.Id;
            job.EstimatedStartDate = plannedStart;
            job.EstimatedCompletionDate = plannedEnd;
            job.UpdatedAt = generatedAt;
        }

        await _db.SaveChangesAsync(ct);
        await RecordBaselineAsync(generatedAt, ct);
        var pencilled = routable.Count(j => !IsBooked(j)) + frozenJobs.Values.Count(j => !IsBooked(j));
        return new ScheduleGenerationResult(routable.Count, scheduleItemsWritten, skipped, frozenPlans.Count, pencilled);
    }

    // Keeps today's baseline (what each line was meant to make by any time today, see PlanBaselineSegment):
    // the part of the day already gone stays as it was planned, the part still ahead follows the new plan.
    // A line's first plan of the day is taken whole.
    private async Task RecordBaselineAsync(DateTime generatedAt, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(generatedAt);
        _db.PlanBaselineSegments.RemoveRange(await _db.PlanBaselineSegments.Where(b => b.Day < today.AddDays(-60)).ToListAsync(ct));

        var kept = await _db.PlanBaselineSegments.Where(b => b.Day == today).ToListAsync(ct);
        var hasBaseline = kept.Select(b => b.StationId).ToHashSet();
        foreach (var b in kept)
        {
            if (b.Start >= generatedAt)
            {
                _db.PlanBaselineSegments.Remove(b);
            }
            else if (b.End > generatedAt)
            {
                b.PlannedEqA *= Share(b.Start, b.End, b.Start, generatedAt);
                b.End = generatedAt;
            }
        }

        var plan = await _db.ScheduleSegments.AsNoTracking()
            .Where(s => s.SegmentDate == today && !s.ScheduleItem.IsArchived)
            .Select(s => new { s.ScheduleItem.StationId, s.ScheduleItem.JobRef, s.SegmentStart, s.SegmentEnd, s.PlannedEqA })
            .ToListAsync(ct);
        foreach (var s in plan)
        {
            var start = hasBaseline.Contains(s.StationId) && s.SegmentStart < generatedAt ? generatedAt : s.SegmentStart;
            if (s.SegmentEnd <= start)
            {
                continue;
            }

            _db.PlanBaselineSegments.Add(new PlanBaselineSegment
            {
                StationId = s.StationId,
                JobRef = s.JobRef,
                Day = today,
                Start = start,
                End = s.SegmentEnd,
                PlannedEqA = s.PlannedEqA * Share(s.SegmentStart, s.SegmentEnd, start, s.SegmentEnd),
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    // The fraction of [start, end) that [from, to) covers.
    private static decimal Share(DateTime start, DateTime end, DateTime from, DateTime to) =>
        end <= start ? 0 : (decimal)((to - from).TotalMinutes / (end - start).TotalMinutes);

    // The earliest start at or after `notBefore` where the whole job fits on the line without running into
    // work already there, and where it would then finish.
    private static (DateTime Start, DateTime End) FirstFit(Line line, double eqa, DateTime notBefore, IReadOnlyList<BreakWindow> breaks)
    {
        var minutes = eqa * line.MinsPerEqa;
        var t = WorkingCalendar.AdvanceToWorkingWindow(notBefore, line.WindowFor, breaks);
        for (var guard = 0; guard < 10_000; guard++)
        {
            var end = minutes > 0 ? WorkingCalendar.PlaceDuration(t, minutes, line.WindowFor, breaks) : t;
            var start = t;
            var clash = line.Busy.Where(b => b.Start < end && b.End > start).OrderBy(b => b.End).LastOrDefault();
            if (clash == default)
            {
                return (t, end);
            }

            t = WorkingCalendar.AdvanceToWorkingWindow(clash.End, line.WindowFor, breaks);
        }

        throw new InvalidOperationException($"Couldn't find room on {line.Station.Name}.");
    }

    // Swaps an item's segments from `from` onward for `replacement`: earlier ones are kept, and one that
    // straddles `from` is cut short there (with its EqA scaled to the part kept).
    private void ReplaceSegmentsFrom(ScheduleItem item, DateTime from, List<WorkSegment> replacement)
    {
        foreach (var segment in item.Segments.ToList())
        {
            if (segment.SegmentStart >= from)
            {
                item.Segments.Remove(segment);
                _db.ScheduleSegments.Remove(segment);
            }
            else if (segment.SegmentEnd > from)
            {
                var kept = (from - segment.SegmentStart).TotalMinutes / (segment.SegmentEnd - segment.SegmentStart).TotalMinutes;
                segment.PlannedEqA = Math.Round(segment.PlannedEqA * (decimal)kept, 4);
                segment.SegmentEnd = from;
            }
        }

        foreach (var s in replacement)
        {
            item.Segments.Add(new ScheduleSegment { SegmentDate = s.Day, SegmentStart = s.Start, SegmentEnd = s.End, PlannedEqA = (decimal)s.Eqa });
        }

        // Each run cuts a running job's plan at "now"; join the pieces that now touch, so repeated runs
        // don't leave a stack of slivers on the Calendar.
        ScheduleSegment? previous = null;
        foreach (var segment in item.Segments.OrderBy(sg => sg.SegmentStart).ToList())
        {
            if (previous is not null && previous.SegmentDate == segment.SegmentDate && previous.SegmentEnd == segment.SegmentStart)
            {
                previous.SegmentEnd = segment.SegmentEnd;
                previous.PlannedEqA += segment.PlannedEqA;
                item.Segments.Remove(segment);
                var entry = _db.Entry(segment);
                if (entry.State == EntityState.Added)
                {
                    entry.State = EntityState.Detached; // never saved: just drop it
                }
                else if (segment.Id != 0)
                {
                    _db.ScheduleSegments.Remove(segment);
                }

                continue;
            }

            previous = segment;
        }
    }
}
