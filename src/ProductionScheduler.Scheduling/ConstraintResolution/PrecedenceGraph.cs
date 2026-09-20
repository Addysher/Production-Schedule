namespace ProductionScheduler.Scheduling.ConstraintResolution;

public class PrecedenceCycleException : Exception
{
    public PrecedenceCycleException(string message) : base(message)
    {
    }
}

// Builds a DAG from job dependency edges and returns a topologically sorted execution order.
// A successor cannot start until its predecessor is complete (§3.2 / §4.2 Precedences table).
public static class PrecedenceGraph
{
    // Kahn's algorithm. Whenever several jobs are free to run (no unfinished predecessors), the one
    // with the smallest `priority` goes first — pass the due date to get "EDD subject to precedence".
    // Without a priority the free jobs are taken in job-reference order, so the result is deterministic.
    public static List<string> TopologicalSort(
        IEnumerable<string> jobRefs,
        IEnumerable<(string PredecessorJobRef, string SuccessorJobRef)> edges,
        Func<string, IComparable>? priority = null)
    {
        var nodes = new HashSet<string>(jobRefs);
        var adjacency = nodes.ToDictionary(n => n, _ => new List<string>());
        var inDegree = nodes.ToDictionary(n => n, _ => 0);

        foreach (var (predecessor, successor) in edges)
        {
            if (predecessor == successor)
            {
                throw new InvalidOperationException($"Job '{predecessor}' cannot precede itself.");
            }

            if (!nodes.Contains(predecessor) || !nodes.Contains(successor))
            {
                throw new InvalidOperationException(
                    $"Precedence edge ({predecessor} -> {successor}) references a job not present in the schedule.");
            }

            adjacency[predecessor].Add(successor);
            inDegree[successor]++;
        }

        var available = nodes.Where(n => inDegree[n] == 0).ToList();
        var sorted = new List<string>(nodes.Count);

        while (available.Count > 0)
        {
            var next = (priority is null
                    ? available.OrderBy(n => n, StringComparer.Ordinal)
                    : available.OrderBy(priority).ThenBy(n => n, StringComparer.Ordinal))
                .First();
            available.Remove(next);
            sorted.Add(next);

            foreach (var successor in adjacency[next])
            {
                inDegree[successor]--;
                if (inDegree[successor] == 0)
                {
                    available.Add(successor);
                }
            }
        }

        if (sorted.Count != nodes.Count)
        {
            throw new PrecedenceCycleException("Precedence graph contains a cycle.");
        }

        return sorted;
    }
}
