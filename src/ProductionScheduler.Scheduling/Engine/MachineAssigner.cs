namespace ProductionScheduler.Scheduling.Engine;

public record MachineAssignerJob(
    string JobRef,
    string ComponentType,
    decimal DurationMinutes,
    bool IsManuallyScheduled,
    int? ManualStationId);

public record MachineAssignerStation(int StationId, string ComponentType, bool IsActive);

public record MachineAssignment(string JobRef, int StationId);

// Step 1: lock in fixed (manually pinned) job-station assignments.
// Step 2: for flexible jobs, assign to the lowest-load station among those matching the job's
// ComponentType (§4.4 — a job can only go to a station whose ComponentType matches).
public static class MachineAssigner
{
    public static IReadOnlyList<MachineAssignment> Assign(
        IEnumerable<MachineAssignerJob> jobs,
        IEnumerable<MachineAssignerStation> stations)
    {
        var activeStations = stations.Where(s => s.IsActive).ToList();
        var load = activeStations.ToDictionary(s => s.StationId, _ => 0m);
        var assignments = new List<MachineAssignment>();
        var jobList = jobs.ToList();

        foreach (var job in jobList.Where(j => j.IsManuallyScheduled))
        {
            if (job.ManualStationId is not int stationId)
            {
                throw new InvalidOperationException(
                    $"Job '{job.JobRef}' is marked manually scheduled but has no ManualStationId.");
            }

            assignments.Add(new MachineAssignment(job.JobRef, stationId));
            if (load.ContainsKey(stationId))
            {
                load[stationId] += job.DurationMinutes;
            }
        }

        foreach (var job in jobList.Where(j => !j.IsManuallyScheduled))
        {
            var compatible = activeStations.Where(s => s.ComponentType == job.ComponentType).ToList();
            if (compatible.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No active station found for component type '{job.ComponentType}' (job '{job.JobRef}').");
            }

            var chosen = compatible.OrderBy(s => load[s.StationId]).ThenBy(s => s.StationId).First();
            assignments.Add(new MachineAssignment(job.JobRef, chosen.StationId));
            load[chosen.StationId] += job.DurationMinutes;
        }

        return assignments;
    }
}
