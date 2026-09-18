namespace ProductionScheduler.Scheduling.Engine;

// Weight defaults to 1 (equal weighting) — the §4.2 schema has no per-job priority/weight column,
// so "total weighted tardiness" reduces to total tardiness unless a caller supplies weights explicitly.
public record TardinessJob(string JobRef, DateTime DueDate, DateTime CompletionTime, decimal Weight = 1m);

public static class TardinessCalc
{
    public static decimal TotalWeightedTardinessMinutes(IEnumerable<TardinessJob> jobs)
        => jobs.Sum(j => j.Weight * (decimal)Math.Max(0, (j.CompletionTime - j.DueDate).TotalMinutes));

    public static double MaxTardinessMinutes(IEnumerable<TardinessJob> jobs)
        => jobs.Select(j => Math.Max(0, (j.CompletionTime - j.DueDate).TotalMinutes)).DefaultIfEmpty(0).Max();

    public static int LateJobCount(IEnumerable<TardinessJob> jobs)
        => jobs.Count(j => j.CompletionTime > j.DueDate);
}
