using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.ConstraintResolution;
using ProductionScheduler.Scheduling.Engine;

namespace ProductionScheduler.Scheduling;

public record ScheduleGenerationResult(int JobsScheduled, int ScheduleItemsWritten);

public interface IScheduleGenerationService
{
    Task<ScheduleGenerationResult> GenerateAsync(CancellationToken ct = default);
}

// Orchestrates the pure engine components (MachineAssigner, Sequencer, PrecedenceGraph, SetupResolver)
// against live data and persists the result to ScheduleItems / Jobs.
//
// Scope: only jobs still in the pre-production queue (FinalDesign, Status in 'ready'/'approved') are
// (re)planned. Jobs already 'cut' or 'in-production' are left untouched — their ScheduleItems row is
// owned by the PRD reader's actuals tracking (§5.9), and creep-flagged-unapproved jobs are frozen per
// §5.14 ("the scheduler will not move this job until CreepApprovedAt IS NOT NULL").
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

        var jobs = await _db.Jobs
            .Where(j => !j.IsArchived
                && j.DesignStatus == "FinalDesign"
                && (j.Status == "ready" || j.Status == "approved")
                && j.ComponentType != null)
            .ToListAsync(ct);

        // Creep-flagged-unapproved jobs are frozen in place — exclude from replanning.
        var frozenJobRefs = await _db.ScheduleItems
            .Where(si => si.CreepDetected && si.CreepApprovedAt == null)
            .Select(si => si.JobRef)
            .ToListAsync(ct);
        jobs = jobs.Where(j => !frozenJobRefs.Contains(j.JobRef)).ToList();

        if (jobs.Count == 0 || stations.Count == 0)
        {
            return new ScheduleGenerationResult(0, 0);
        }

        var jobTypesByComponent = await _db.JobTypes
            .Where(jt => jt.IsActive && jt.ComponentType != null)
            .AsNoTracking()
            .ToListAsync(ct);

        var settings = await _db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        var shiftStartHour = int.Parse(settings.GetValueOrDefault("ShiftStartHour", "6"));
        var shiftEndHour = int.Parse(settings.GetValueOrDefault("ShiftEndHour", "18"));
        var workingDays = settings.GetValueOrDefault("WorkingDays", "1,2,3,4,5")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToHashSet();

        decimal DurationMinutesFor(Job job)
        {
            var minsPerUnit = jobTypesByComponent.FirstOrDefault(jt => jt.ComponentType == job.ComponentType)?.MinsPerUnit;
            var eqa = job.TotalEqA ?? 0;
            return minsPerUnit is decimal m ? eqa * m : eqa; // fall back to 1 minute per EqA if unconfigured
        }

        DateTime DueDateFor(Job job) =>
            (job.EstBookedDeliveryDate ?? job.ExpectedDeliveryDate)?.ToDateTime(TimeOnly.MinValue)
            ?? DateTime.UtcNow.AddDays(14);

        var assignerJobs = jobs
            .Select(j => new MachineAssignerJob(j.JobRef, j.ComponentType!, DurationMinutesFor(j), j.IsManuallyScheduled, j.ManualStationId))
            .ToList();
        var assignerStations = stations
            .Select(s => new MachineAssignerStation(s.Id, s.ComponentType, s.IsActive))
            .ToList();
        var assignments = MachineAssigner.Assign(assignerJobs, assignerStations);
        var jobsByRef = jobs.ToDictionary(j => j.JobRef);

        var precedenceEdges = await _db.Precedences
            .AsNoTracking()
            .Select(p => new { p.PredecessorJobRef, p.SuccessorJobRef })
            .ToListAsync(ct);

        var generatedAt = DateTime.UtcNow;
        var scheduleStart = generatedAt;
        var scheduleItemsWritten = 0;

        foreach (var stationGroup in assignments.GroupBy(a => a.StationId))
        {
            var stationJobRefs = stationGroup.Select(a => a.JobRef).ToHashSet();
            var stationJobs = stationJobRefs.Select(r => jobsByRef[r]).ToList();

            var relevantEdges = precedenceEdges
                .Where(e => stationJobRefs.Contains(e.PredecessorJobRef) && stationJobRefs.Contains(e.SuccessorJobRef))
                .Select(e => (e.PredecessorJobRef, e.SuccessorJobRef))
                .ToList();

            // Precedence constraints (when present) take priority over pure due-date ordering —
            // PrecedenceGraph guarantees a valid dependency order; EDD alone cannot.
            List<string> order;
            if (relevantEdges.Count > 0)
            {
                order = PrecedenceGraph.TopologicalSort(stationJobRefs, relevantEdges);
            }
            else
            {
                var sequencerJobs = stationJobs
                    .Select(j => new SequencerJob(j.JobRef, j.ComponentType!, DueDateFor(j), DurationMinutesFor(j)))
                    .ToList();
                order = Sequencer.SequenceEdd(sequencerJobs);
            }

            var cursor = WorkingCalendar.AdvanceToWorkingWindow(scheduleStart, shiftStartHour, shiftEndHour, workingDays);
            string? previousComponentType = null;

            foreach (var jobRef in order)
            {
                var job = jobsByRef[jobRef];
                DateTime plannedStart, plannedEnd;

                if (job.IsManuallyScheduled && job.ManualScheduleDate is DateOnly manualDate)
                {
                    // A pinned job's placement is fixed by the admin and does not consume/advance
                    // the shared cursor — other flexible jobs on this station schedule around it.
                    plannedStart = WorkingCalendar.AdvanceToWorkingWindow(
                        manualDate.ToDateTime(new TimeOnly(shiftStartHour, 0)), shiftStartHour, shiftEndHour, workingDays);
                    plannedEnd = WorkingCalendar.PlaceDuration(plannedStart, (double)DurationMinutesFor(job), shiftStartHour, shiftEndHour, workingDays);
                }
                else
                {
                    var setupMinutes = previousComponentType is null
                        ? 0
                        : await _setupResolver.GetSetupMinutesAsync(previousComponentType, job.ComponentType!, ct);

                    if (setupMinutes > 0)
                    {
                        cursor = WorkingCalendar.PlaceDuration(cursor, setupMinutes, shiftStartHour, shiftEndHour, workingDays);
                    }

                    plannedStart = WorkingCalendar.AdvanceToWorkingWindow(cursor, shiftStartHour, shiftEndHour, workingDays);
                    plannedEnd = WorkingCalendar.PlaceDuration(plannedStart, (double)DurationMinutesFor(job), shiftStartHour, shiftEndHour, workingDays);
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
                    StationId = stationGroup.Key,
                    ScheduleDate = DateOnly.FromDateTime(plannedStart),
                    PlannedStart = plannedStart,
                    PlannedEnd = plannedEnd,
                    PlannedEqA = job.TotalEqA ?? 0,
                    MembersTotal = await _db.JobMembers.CountAsync(m => m.JobRef == jobRef, ct),
                    GeneratedAt = generatedAt
                });
                scheduleItemsWritten++;

                job.AssignedStationId = stationGroup.Key;
                job.EstimatedStartDate = plannedStart;
                job.EstimatedCompletionDate = plannedEnd;
                job.UpdatedAt = generatedAt;
            }
        }

        await _db.SaveChangesAsync(ct);
        return new ScheduleGenerationResult(jobs.Count, scheduleItemsWritten);
    }
}
