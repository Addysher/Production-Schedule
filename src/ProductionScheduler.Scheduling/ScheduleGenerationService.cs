using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.ConstraintResolution;
using ProductionScheduler.Scheduling.Engine;

namespace ProductionScheduler.Scheduling;

// JobsSkipped = queued jobs with no active station to run on (left unassigned rather than aborting the run).
public record ScheduleGenerationResult(int JobsScheduled, int ScheduleItemsWritten, int JobsSkipped = 0);

public interface IScheduleGenerationService
{
    Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default);
}

// Orchestrates the pure engine components (MachineAssigner, Sequencer, PrecedenceGraph, SetupResolver)
// against live data and persists the result to ScheduleItems / Jobs.
//
// Queued work (FinalDesign, status 'ready'/'approved') is (re)planned on every run. Work already on the
// floor ('cut' / 'in-production') is never re-planned, but it does occupy its station: queued jobs start
// after its projected end. That projection is:
//   * creep flagged and not yet approved -> the frozen plan (§5.14: "the scheduler will not move this job
//     until CreepApprovedAt IS NOT NULL"), so a slipping job doesn't silently drag the queue behind it;
//   * otherwise                          -> the work still to do (TotalEqA - CompletedEqA) placed from now,
//     which is what makes approving creep actually change the schedule.
// The original ScheduleItem plan is never edited, so plan-vs-actual history survives.
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

    public async Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default)
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
                    && (j.Status == "ready" || j.Status == "approved")
                    && j.ComponentType != null)
                .ToListAsync(ct))
            .Where(j => !frozenPlanEnds.ContainsKey(j.JobRef))
            .ToList();

        var inFlight = await _db.Jobs
            .Where(j => !j.IsArchived && (j.Status == "cut" || j.Status == "in-production") && j.AssignedStationId != null)
            .OrderBy(j => j.EstimatedStartDate)
            .ToListAsync(ct);

        var jobTypes = await _db.JobTypes.Where(jt => jt.IsActive && jt.ComponentType != null).AsNoTracking().ToListAsync(ct);

        var calendar = await WorkCalendarSettings.LoadAsync(_db, ct);
        var shiftStartHour = calendar.ShiftStartHour;
        var shiftEndHour = calendar.ShiftEndHour;
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
        var windowStart = WorkingCalendar.AdvanceToWorkingWindow(generatedAt, shiftStartHour, shiftEndHour, workingDays, breaks);
        var usedEqaByStation = stations.ToDictionary(s => s.Id, _ => new Dictionary<DateOnly, double>());
        var busyUntil = stations.ToDictionary(s => s.Id, _ => windowStart);

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
                var end = WorkingCalendar.PlaceWork(busyUntil[stationId], remainingEqa, MinsPerEqa(job),
                    shiftStartHour, shiftEndHour, workingDays, CapacityFor(station), usedEqaByStation[stationId], breaks);

                if (job.TotalEqA is > 0)
                {
                    job.EstimatedCompletionDate = end;
                    job.UpdatedAt = generatedAt;
                }

                projectedEnd = end > projectedEnd ? end : projectedEnd;
            }

            busyUntil[stationId] = projectedEnd;
        }

        // --- Queued work: route what can be routed, skip (and report) what cannot. ---
        bool IsEffectivelyPinned(Job j) => j.IsManuallyScheduled && j.ManualStationId is int id && activeStationIds.Contains(id);
        bool IsRoutable(Job j) => IsEffectivelyPinned(j) || stations.Any(s => s.ComponentType == j.ComponentType);

        var routable = queued.Where(IsRoutable).ToList();
        var skipped = queued.Count - routable.Count;
        var jobsByRef = routable.ToDictionary(j => j.JobRef);

        var assignments = MachineAssigner.Assign(
            routable.Select(j => new MachineAssignerJob(
                j.JobRef, j.ComponentType!, DurationMinutesFor(j), IsEffectivelyPinned(j), j.ManualStationId)),
            stations.Select(s => new MachineAssignerStation(s.Id, s.ComponentType, s.IsActive)));

        var precedenceEdges = await _db.Precedences
            .AsNoTracking()
            .Select(p => new { p.PredecessorJobRef, p.SuccessorJobRef })
            .ToListAsync(ct);

        var scheduleItemsWritten = 0;

        foreach (var stationGroup in assignments.GroupBy(a => a.StationId))
        {
            var station = stations.Single(s => s.Id == stationGroup.Key);
            var stationJobRefs = stationGroup.Select(a => a.JobRef).ToHashSet();
            var stationJobs = stationJobRefs.Select(r => jobsByRef[r]).ToList();

            var relevantEdges = precedenceEdges
                .Where(e => stationJobRefs.Contains(e.PredecessorJobRef) && stationJobRefs.Contains(e.SuccessorJobRef))
                .Select(e => (e.PredecessorJobRef, e.SuccessorJobRef))
                .ToList();

            // EDD is the primary sequencing rule; precedence edges are a hard constraint on top of it.
            List<string> order;
            if (relevantEdges.Count > 0)
            {
                order = PrecedenceGraph.TopologicalSort(stationJobRefs, relevantEdges, r => DueDateFor(jobsByRef[r]));
            }
            else
            {
                order = Sequencer.SequenceEdd(stationJobs
                    .Select(j => new SequencerJob(j.JobRef, j.ComponentType!, DueDateFor(j), DurationMinutesFor(j))));
            }

            var cursor = busyUntil[station.Id];
            string? previousComponentType = null;

            foreach (var jobRef in order)
            {
                var job = jobsByRef[jobRef];
                DateTime plannedStart, plannedEnd;
                var eqa = (double)(job.TotalEqA ?? 0);
                var segments = new List<WorkSegment>();

                if (IsEffectivelyPinned(job) && job.ManualScheduleDate is DateOnly manualDate)
                {
                    // A pinned job's placement is fixed by the admin and does not consume/advance
                    // the shared cursor — other flexible jobs on this station schedule around it.
                    plannedStart = WorkingCalendar.AdvanceToWorkingWindow(
                        manualDate.ToDateTime(new TimeOnly(shiftStartHour, 0)), shiftStartHour, shiftEndHour, workingDays, breaks);
                    plannedEnd = WorkingCalendar.PlaceWork(plannedStart, eqa, MinsPerEqa(job),
                        shiftStartHour, shiftEndHour, workingDays, CapacityFor(station), usedEqaByStation[station.Id], breaks, segments);
                }
                else
                {
                    var setupMinutes = previousComponentType is null
                        ? 0
                        : await _setupResolver.GetSetupMinutesAsync(previousComponentType, job.ComponentType!, ct);

                    if (setupMinutes > 0)
                    {
                        cursor = WorkingCalendar.PlaceDuration(cursor, setupMinutes, shiftStartHour, shiftEndHour, workingDays, breaks);
                    }

                    plannedStart = WorkingCalendar.AdvanceToWorkingWindow(cursor, shiftStartHour, shiftEndHour, workingDays, breaks);
                    plannedEnd = WorkingCalendar.PlaceWork(plannedStart, eqa, MinsPerEqa(job),
                        shiftStartHour, shiftEndHour, workingDays, CapacityFor(station), usedEqaByStation[station.Id], breaks, segments);
                    cursor = plannedEnd;
                    previousComponentType = job.ComponentType;
                }

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
        return new ScheduleGenerationResult(routable.Count, scheduleItemsWritten, skipped);
    }
}
