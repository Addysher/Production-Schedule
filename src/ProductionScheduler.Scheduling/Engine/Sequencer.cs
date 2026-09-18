namespace ProductionScheduler.Scheduling.Engine;

public record SequencerJob(string JobRef, string ComponentType, DateTime DueDate, decimal ProcessingMinutes);

// EDD (primary heuristic) and ATCS (Apparent Tardiness Cost with Setups, an enhancement over EDD
// used when setup times between component types are significant) — see §3.3.
public static class Sequencer
{
    public static List<string> SequenceEdd(IEnumerable<SequencerJob> jobs)
        => jobs.OrderBy(j => j.DueDate).ThenBy(j => j.JobRef, StringComparer.Ordinal).Select(j => j.JobRef).ToList();

    // setupMinutes(fromComponentType, toComponentType) -> changeover duration, e.g. backed by SetupResolver.
    public static List<string> SequenceAtcs(
        IReadOnlyCollection<SequencerJob> jobs,
        Func<string, string, int> setupMinutes,
        DateTime scheduleStart,
        double k1 = 1.5,
        double k2 = 1.5)
    {
        if (jobs.Count == 0)
        {
            return new List<string>();
        }

        var avgProcessing = jobs.Average(j => (double)j.ProcessingMinutes);
        var avgSetup = AverageSetupAcrossComponentTypes(jobs, setupMinutes);
        if (avgSetup <= 0)
        {
            avgSetup = 1; // no configured setups — avoid dividing by zero; setup term then has no effect
        }

        var remaining = jobs.ToList();
        var sequence = new List<string>(remaining.Count);
        string? currentComponentType = null;
        var t = scheduleStart;

        while (remaining.Count > 0)
        {
            SequencerJob? best = null;
            var bestPriority = double.NegativeInfinity;
            var bestSetup = 0;

            foreach (var job in remaining)
            {
                var setup = currentComponentType is null ? 0 : setupMinutes(currentComponentType, job.ComponentType);
                var slackMinutes = Math.Max((job.DueDate - t).TotalMinutes - (double)job.ProcessingMinutes, 0);

                var priority = (1.0 / (double)job.ProcessingMinutes)
                    * Math.Exp(-slackMinutes / (k1 * avgProcessing))
                    * Math.Exp(-setup / (k2 * avgSetup));

                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    best = job;
                    bestSetup = setup;
                }
            }

            sequence.Add(best!.JobRef);
            t = t.AddMinutes(bestSetup + (double)best.ProcessingMinutes);
            currentComponentType = best.ComponentType;
            remaining.Remove(best);
        }

        return sequence;
    }

    private static double AverageSetupAcrossComponentTypes(
        IReadOnlyCollection<SequencerJob> jobs,
        Func<string, string, int> setupMinutes)
    {
        var types = jobs.Select(j => j.ComponentType).Distinct().ToList();
        var pairs = new List<int>();

        foreach (var from in types)
        {
            foreach (var to in types)
            {
                if (from != to)
                {
                    pairs.Add(setupMinutes(from, to));
                }
            }
        }

        return pairs.Count == 0 ? 0 : pairs.Average();
    }
}
