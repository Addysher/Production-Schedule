using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.ConstraintResolution;
using ProductionScheduler.Scheduling.Engine;

namespace ProductionScheduler.Scheduling;

// JobsSkipped = queued jobs with no active station to run on (left unassigned rather than aborting the run).
// JobsFrozen = jobs left exactly where they were because they start inside the freeze window.
public record ScheduleGenerationResult(int JobsScheduled, int ScheduleItemsWritten, int JobsSkipped = 0, int JobsFrozen = 0);

public interface IScheduleGenerationService
{
    Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default);
}

// Orchestrates the pure engine components (MachineAssigner, Sequencer, PrecedenceGraph, SetupResolver)
// against live data and persists the result to ScheduleItems / Jobs.
//
// Every job that hasn't started production (FinalDesign, status 'ready'/'approved'/'cut') is (re)planned on
// every run. A 'cut' job stays on the station it was cut for and at the front of that station's queue,
// unless someone moves it. Work already running ('in-production') is never re-planned, but it does occupy
// its station: everything else starts after its projected end. That projection is:
//   * creep flagged and not yet approved -> the frozen plan (§5.14: "the scheduler will not move this job
//     until CreepApprovedAt IS NOT NULL"), so a slipping job doesn't silently drag the queue behind it;
//   * otherwise                          -> the work still to do (TotalEqA - CompletedEqA) placed from now,
//     which is what makes approving creep actually change the schedule.
// A running job's original ScheduleItem plan is never edited, so plan-vs-actual history survives.
//
// Pinned jobs (Schedule manually / Calendar drag-and-drop) start on their pinned day — or as soon as the
// station is free after it — and the station's other work flows around them in one sequence, so nothing
// overlaps: a job that wouldn't finish before a pinned job's day waits until after it.
//
// Capacity: a station's per-day EqA limit is its StationCapacityOverride for that date if one exists,
// otherwise Stations.DailyEqATarget; null means unlimited.
//
// Known limitation: precedence is enforced between jobs on the same station only.
public class ScheduleGenerationService : IScheduleGenerationService
{
    private readonly AppDbContext _db;
    private readonly ISetupResolver _setupResolver;

    public ScheduleGenerationService(AppDbContext db, ISetupResolver setupResolver)
    {
        _db = db;
        _setupResolver = setupResolver;
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

    private async Task<ScheduleGenerationResult> GenerateCoreAsync(CancellationToken ct)
    {
        var stations = await _db.Stations.Where(s => s.IsActive).AsNoTracking().ToListAsync(ct);
        if (stations.Count == 0)
        {
            return new ScheduleGenerationResult(0, 0);
        }

        var activeStationIds = stations.Select(s => s.Id).ToHashSet();

        var frozenPlanEnds = (await _db.ScheduleItems
                .Where(si => !si.IsArchived && si.CreepDetected && si.CreepApprovedAt == null)
                .Select(si => new { si.JobRef, si.PlannedEnd })
                .ToListAsync(ct))
            .GroupBy(x => x.JobRef)
            .ToDictionary(g => g.Key, g => g.Max(x => x.PlannedEnd));

        var queued = (await _db.Jobs
                .Where(j => !j.IsArchived
                    && j.DesignStatus == "FinalDesign"
                    && (j.Status == "ready" || j.Status == "approved" || j.Status == "cut")
                    && j.ComponentType != null)
                .ToListAsync(ct))
            .Where(j => !frozenPlanEnds.ContainsKey(j.JobRef))
            .ToList();

        var inFlight = await _db.Jobs
            .Where(j => !j.IsArchived && j.Status == "in-production" && j.AssignedStationId != null)
            .OrderBy(j => j.EstimatedStartDate)
            .ToListAsync(ct);

        var jobTypes = await _db.JobTypes.Where(jt => jt.IsActive && jt.ComponentType != null).AsNoTracking().ToListAsync(ct);

        var calendar = await WorkCalendarSettings.LoadAsync(_db, ct);
        var workStart = calendar.DayStart;
        var workEnd = calendar.DayEnd;
        var workingDays = calendar.WorkingDays;
        var breaks = calendar.Breaks;

        var overrides = (await _db.StationCapacityOverrides.AsNoTracking().ToListAsync(ct))
            .ToDictionary(o => (o.StationId, o.OverrideDate), o => (double)o.EqATarget);

        double MinsPerEqa(Job job) =>
            (double)(jobTypes.FirstOrDefault(jt => jt.ComponentType == job.ComponentType)?.MinsPerUnit ?? 1m); // 1 min per EqA if unconfigured

        decimal DurationMinutesFor(Job job) => (job.TotalEqA ?? 0) * (decimal)MinsPerEqa(job);

        DateTime DueDateFor(Job job) =>
            (job.EstBookedDeliveryDate ?? job.ExpectedDeliveryDate)?.ToDateTime(TimeOnly.MinValue)
            ?? SiteClock.Now.AddDays(14);

        Func<DateOnly, double?> CapacityFor(Station station) => day =>
            overrides.TryGetValue((station.Id, day), out var target) ? target
            : station.DailyEqATarget is decimal daily ? (double)daily
            : null;

        var generatedAt = SiteClock.Now;

        // Freeze window: not-started jobs already planned to start inside it keep their slot (on a station
        // that is still active and still makes their product) and are left out of re-planning entirely.
        var frozenPlans = (await FreezeWindow.FrozenPlansAsync(_db, calendar.FreezeUntil(generatedAt), ct))
            .Where(kv => queued.Any(j => j.JobRef == kv.Key
                && stations.Any(s => s.Id == kv.Value[0].StationId && s.ComponentType == j.ComponentType)))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var frozenJobs = queued.Where(j => frozenPlans.ContainsKey(j.JobRef)).ToDictionary(j => j.JobRef);
        queued = queued.Where(j => !frozenPlans.ContainsKey(j.JobRef)).ToList();

        var windowStart = WorkingCalendar.AdvanceToWorkingWindow(generatedAt, workStart, workEnd, workingDays, breaks);
        var usedEqaByStation = stations.ToDictionary(s => s.Id, _ => new Dictionary<DateOnly, double>());

        // Work already done today counts against today's capacity — otherwise a station that has been
        // busy since the morning is booked for a full day's capacity again from now.
        var today = DateOnly.FromDateTime(generatedAt);
        var dayStart = generatedAt.Date;
        var doneToday = await _db.JobMembers
            .Where(m => m.Done && m.CompletedAt >= dayStart && m.CompletedAt < dayStart.AddDays(1) && m.Job.AssignedStationId != null)
            .GroupBy(m => m.Job.AssignedStationId!.Value)
            .Select(g => new { StationId = g.Key, Eqa = g.Sum(m => m.CompletedEqA ?? m.EqaValue) })
            .ToListAsync(ct);
        foreach (var row in doneToday.Where(r => usedEqaByStation.ContainsKey(r.StationId)))
        {
            usedEqaByStation[row.StationId][today] = (double)row.Eqa;
        }
        var busyUntil = stations.ToDictionary(s => s.Id, _ => windowStart);
        var lineFreeAt = stations.ToDictionary(s => s.Id, _ => DateTime.MinValue); // when running work is projected to finish

        var inFlightRefs = inFlight.Select(j => j.JobRef).ToList();
        var inFlightItems = await _db.ScheduleItems
            .Include(si => si.Segments)
            .Where(si => !si.IsArchived && si.ActualStart != null && si.ActualEnd == null && inFlightRefs.Contains(si.JobRef))
            .ToListAsync(ct);

        // --- Work already on the floor: occupy the station until its projected end. ---
        foreach (var job in inFlight)
        {
            var stationId = job.AssignedStationId!.Value;
            if (!activeStationIds.Contains(stationId))
            {
                continue;
            }

            var station = stations.Single(s => s.Id == stationId);
            var projectedEnd = busyUntil[stationId];

            if (frozenPlanEnds.TryGetValue(job.JobRef, out var frozenEnd))
            {
                projectedEnd = frozenEnd > projectedEnd ? frozenEnd : projectedEnd;
            }
            else
            {
                var remainingEqa = Math.Max((double)((job.TotalEqA ?? 0) - job.CompletedEqA), 0);
                var projected = new List<WorkSegment>();
                var end = WorkingCalendar.PlaceWork(busyUntil[stationId], remainingEqa, MinsPerEqa(job),
                    workStart, workEnd, workingDays, CapacityFor(station), usedEqaByStation[stationId], breaks, projected);

                // The Calendar draws segments, so a running job shows where its remaining work now falls (under
                // today's hours and breaks). What's already past stays, and so does the original plan
                // (PlannedStart/PlannedEnd) for plan-vs-actual.
                if (inFlightItems.FirstOrDefault(i => i.JobRef == job.JobRef) is { } running)
                {
                    ReplaceSegmentsFrom(running, generatedAt, projected);
                }

                if (job.TotalEqA is > 0)
                {
                    job.EstimatedCompletionDate = end;
                    job.UpdatedAt = generatedAt;
                }

                projectedEnd = end > projectedEnd ? end : projectedEnd;
            }

            busyUntil[stationId] = projectedEnd;
            lineFreeAt[stationId] = projectedEnd;
        }

        // --- Frozen work: keeps its station, its order and its start; everything else goes after it. ---
        // Its times are re-shaped to the current working day, so a new break or shorter shift shows on
        // frozen days too. It only moves later if a break now covers its start, or running work is now
        // expected to finish after it; it is never moved by capacity (that was checked when it was planned).
        var frozenRefs = frozenPlans.Keys.ToList();
        var frozenItems = await _db.ScheduleItems
            .Include(si => si.Segments)
            .Where(si => !si.IsArchived && si.ActualStart == null && frozenRefs.Contains(si.JobRef))
            .ToListAsync(ct);
        foreach (var stationItems in frozenItems.GroupBy(i => i.StationId).Where(g => busyUntil.ContainsKey(g.Key)))
        {
            var stationId = stationItems.Key;
            var freeAt = lineFreeAt[stationId];
            foreach (var item in stationItems.OrderBy(i => i.PlannedStart))
            {
                var job = frozenJobs[item.JobRef];
                var start = WorkingCalendar.AdvanceToWorkingWindow(item.PlannedStart > freeAt ? item.PlannedStart : freeAt, workStart, workEnd, workingDays, breaks);
                var placed = new List<WorkSegment>();
                var end = WorkingCalendar.PlaceWork(start, (double)item.PlannedEqA, MinsPerEqa(job),
                    workStart, workEnd, workingDays, _ => null, usedEqaByStation[stationId], breaks, placed);

                item.PlannedStart = placed.Count > 0 ? placed[0].Start : start;
                item.PlannedEnd = end;
                item.ScheduleDate = DateOnly.FromDateTime(item.PlannedStart);
                ReplaceSegmentsFrom(item, DateTime.MinValue, placed);
                job.EstimatedStartDate = item.PlannedStart;
                job.EstimatedCompletionDate = end;
                freeAt = end;
                if (end > busyUntil[stationId])
                {
                    busyUntil[stationId] = end;
                }
            }
        }

        // --- Queued work: route what can be routed, skip (and report) what cannot. ---
        // A pin only holds while its station is active and still makes the job's product (the job's
        // product can be changed after it was pinned).
        bool IsEffectivelyPinned(Job j) => j.IsManuallyScheduled && j.ManualStationId is int id
            && stations.Any(s => s.Id == id && s.ComponentType == j.ComponentType);

        // A pinned job goes to its pinned station; an unmoved cut job stays on the station it was cut for
        // (if that station is still active and still makes its product); everything else is routed.
        int? FixedStation(Job j) =>
            IsEffectivelyPinned(j) ? j.ManualStationId
            : j.Status == "cut" && j.AssignedStationId is int assigned && stations.Any(s => s.Id == assigned && s.ComponentType == j.ComponentType) ? assigned
            : null;
        bool IsRoutable(Job j) => FixedStation(j) is not null || stations.Any(s => s.ComponentType == j.ComponentType);

        var routable = queued.Where(IsRoutable).ToList();
        var skipped = queued.Count - routable.Count;

        // A job with nowhere to run (no active station makes its product) loses its old plan, rather
        // than keeping a slot on a station that can no longer run it — so it shows as unscheduled.
        foreach (var job in queued.Where(j => !IsRoutable(j)))
        {
            _db.ScheduleItems.RemoveRange(await _db.ScheduleItems.Where(si => si.JobRef == job.JobRef && si.ActualStart == null).ToListAsync(ct));
            job.AssignedStationId = null;
            job.EstimatedStartDate = null;
            job.EstimatedCompletionDate = null;
            job.UpdatedAt = generatedAt;
        }
        var jobsByRef = routable.ToDictionary(j => j.JobRef);

        var assignments = MachineAssigner.Assign(
            routable.Select(j => new MachineAssignerJob(
                j.JobRef, j.ComponentType!, DurationMinutesFor(j), FixedStation(j) is not null, FixedStation(j))),
            stations.Select(s => new MachineAssignerStation(s.Id, s.ComponentType, s.IsActive)));

        var precedenceEdges = await _db.Precedences
            .AsNoTracking()
            .Select(p => new { p.PredecessorJobRef, p.SuccessorJobRef })
            .ToListAsync(ct);

        var scheduleItemsWritten = 0;

        foreach (var stationGroup in assignments.GroupBy(a => a.StationId))
        {
            var station = stations.Single(s => s.Id == stationGroup.Key);
            var stationJobs = stationGroup.Select(a => jobsByRef[a.JobRef]).ToList();

            var pinned = stationJobs
                .Where(j => IsEffectivelyPinned(j) && j.ManualScheduleDate is not null)
                .OrderBy(j => j.ManualScheduleDate).ThenBy(DueDateFor)
                .ToList();
            var pinnedRefs = pinned.Select(j => j.JobRef).ToHashSet();

            // Cut jobs are already through the saw, so they keep their place at the front of the line.
            var cutFirst = stationJobs
                .Where(j => !pinnedRefs.Contains(j.JobRef) && j.Status == "cut")
                .OrderBy(j => j.EstimatedStartDate ?? DateTime.MaxValue).ThenBy(DueDateFor)
                .Select(j => j.JobRef);

            var flexibleRefs = stationJobs
                .Where(j => !pinnedRefs.Contains(j.JobRef) && j.Status != "cut")
                .Select(j => j.JobRef)
                .ToHashSet();
            var relevantEdges = precedenceEdges
                .Where(e => flexibleRefs.Contains(e.PredecessorJobRef) && flexibleRefs.Contains(e.SuccessorJobRef))
                .Select(e => (e.PredecessorJobRef, e.SuccessorJobRef))
                .ToList();

            // EDD is the primary sequencing rule; precedence edges are a hard constraint on top of it.
            var flexibleOrder = relevantEdges.Count > 0
                ? PrecedenceGraph.TopologicalSort(flexibleRefs, relevantEdges, r => DueDateFor(jobsByRef[r]))
                : Sequencer.SequenceEdd(flexibleRefs.Select(r => jobsByRef[r])
                    .Select(j => new SequencerJob(j.JobRef, j.ComponentType!, DueDateFor(j), DurationMinutesFor(j))));

            var cursor = busyUntil[station.Id];
            string? previousComponentType = null;
            var usedEqa = usedEqaByStation[station.Id];

            DateTime PinStart(Job j) => WorkingCalendar.AdvanceToWorkingWindow(
                j.ManualScheduleDate!.Value.ToDateTime(TimeOnly.FromTimeSpan(workStart)), workStart, workEnd, workingDays, breaks);

            async Task<DateTime> EarliestStartAsync(Job job, DateTime? notBefore)
            {
                var setupMinutes = previousComponentType is null
                    ? 0
                    : await _setupResolver.GetSetupMinutesAsync(previousComponentType, job.ComponentType!, ct);
                var t = setupMinutes > 0
                    ? WorkingCalendar.PlaceDuration(cursor, setupMinutes, workStart, workEnd, workingDays, breaks)
                    : cursor;
                if (notBefore is DateTime nb && nb > t)
                {
                    t = nb;
                }

                return WorkingCalendar.AdvanceToWorkingWindow(t, workStart, workEnd, workingDays, breaks);
            }

            // Dry run on a copy of the day-capacity tally, so asking "would it fit?" books nothing.
            DateTime WouldEnd(Job job, DateTime start) => WorkingCalendar.PlaceWork(start, (double)(job.TotalEqA ?? 0), MinsPerEqa(job),
                workStart, workEnd, workingDays, CapacityFor(station), new Dictionary<DateOnly, double>(usedEqa), breaks);

            var pinQueue = new Queue<Job>(pinned);
            foreach (var jobRef in cutFirst.Concat(flexibleOrder))
            {
                var job = jobsByRef[jobRef];
                while (pinQueue.Count > 0)
                {
                    var pin = pinQueue.Peek();
                    var pinStart = PinStart(pin);
                    var start = await EarliestStartAsync(job, null);
                    if (pinStart > start && WouldEnd(job, start) <= pinStart)
                    {
                        break; // this job finishes before the pinned day, so it goes first
                    }

                    pinQueue.Dequeue();
                    await PlaceAsync(pin, pinStart);
                }

                await PlaceAsync(job, null);
            }

            while (pinQueue.Count > 0)
            {
                var pin = pinQueue.Dequeue();
                await PlaceAsync(pin, PinStart(pin));
            }

            async Task PlaceAsync(Job job, DateTime? notBefore)
            {
                var jobRef = job.JobRef;
                var segments = new List<WorkSegment>();
                var plannedStart = await EarliestStartAsync(job, notBefore);
                var plannedEnd = WorkingCalendar.PlaceWork(plannedStart, (double)(job.TotalEqA ?? 0), MinsPerEqa(job),
                    workStart, workEnd, workingDays, CapacityFor(station), usedEqa, breaks, segments);

                // A full day (capacity) pushes the work to a later day — the plan starts where the work does.
                if (segments.Count > 0)
                {
                    plannedStart = segments[0].Start;
                }

                cursor = plannedEnd;
                previousComponentType = job.ComponentType;

                // Replace any existing (not-yet-started) plan for this job — the schedule is being recalculated.
                var existing = await _db.ScheduleItems
                    .Where(si => si.JobRef == jobRef && si.ActualStart == null)
                    .ToListAsync(ct);
                _db.ScheduleItems.RemoveRange(existing);

                _db.ScheduleItems.Add(new ScheduleItem
                {
                    JobRef = jobRef,
                    StationId = station.Id,
                    ScheduleDate = DateOnly.FromDateTime(plannedStart),
                    PlannedStart = plannedStart,
                    PlannedEnd = plannedEnd,
                    PlannedEqA = job.TotalEqA ?? 0,
                    MembersTotal = await _db.JobMembers.CountAsync(m => m.JobRef == jobRef, ct),
                    Segments = segments
                        .Select(s => new ScheduleSegment { SegmentDate = s.Day, SegmentStart = s.Start, SegmentEnd = s.End, PlannedEqA = (decimal)s.Eqa })
                        .ToList(),
                    GeneratedAt = generatedAt
                });
                scheduleItemsWritten++;

                job.AssignedStationId = station.Id;
                job.EstimatedStartDate = plannedStart;
                job.EstimatedCompletionDate = plannedEnd;
                job.UpdatedAt = generatedAt;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new ScheduleGenerationResult(routable.Count, scheduleItemsWritten, skipped, frozenPlans.Count);
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
    }
}
