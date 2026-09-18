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
    public static List<string> TopologicalSort(
        IEnumerable<string> jobRefs,
        IEnumerable<(string PredecessorJobRef, string SuccessorJobRef)> edges)
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

        var queue = new Queue<string>(nodes.Where(n => inDegree[n] == 0));
        var sorted = new List<string>();

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            sorted.Add(current);

            foreach (var next in adjacency[current])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    queue.Enqueue(next);
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
