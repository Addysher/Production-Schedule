using ProductionScheduler.Scheduling.ConstraintResolution;
using Xunit;

namespace ProductionScheduler.Tests.ConstraintResolution;

public class PrecedenceGraphTests
{
    [Fact]
    public void TopologicalSort_OrdersPredecessorsBeforeSuccessors()
    {
        var jobRefs = new[] { "A", "B", "C" };
        var edges = new[] { ("A", "B"), ("B", "C") };

        var sorted = PrecedenceGraph.TopologicalSort(jobRefs, edges);

        Assert.Equal(3, sorted.Count);
        Assert.True(sorted.IndexOf("A") < sorted.IndexOf("B"));
        Assert.True(sorted.IndexOf("B") < sorted.IndexOf("C"));
    }

    [Fact]
    public void TopologicalSort_NoEdges_ReturnsAllJobsInAnyOrder()
    {
        var jobRefs = new[] { "A", "B", "C" };

        var sorted = PrecedenceGraph.TopologicalSort(jobRefs, Array.Empty<(string, string)>());

        Assert.Equal(jobRefs.ToHashSet(), sorted.ToHashSet());
    }

    [Fact]
    public void TopologicalSort_DiamondDependency_RespectsAllEdges()
    {
        // A -> B -> D
        // A -> C -> D
        var jobRefs = new[] { "A", "B", "C", "D" };
        var edges = new[] { ("A", "B"), ("A", "C"), ("B", "D"), ("C", "D") };

        var sorted = PrecedenceGraph.TopologicalSort(jobRefs, edges);

        Assert.True(sorted.IndexOf("A") < sorted.IndexOf("B"));
        Assert.True(sorted.IndexOf("A") < sorted.IndexOf("C"));
        Assert.True(sorted.IndexOf("B") < sorted.IndexOf("D"));
        Assert.True(sorted.IndexOf("C") < sorted.IndexOf("D"));
    }

    [Fact]
    public void TopologicalSort_CycleInGraph_ThrowsPrecedenceCycleException()
    {
        var jobRefs = new[] { "A", "B", "C" };
        var edges = new[] { ("A", "B"), ("B", "C"), ("C", "A") };

        Assert.Throws<PrecedenceCycleException>(() => PrecedenceGraph.TopologicalSort(jobRefs, edges));
    }

    [Fact]
    public void TopologicalSort_SelfLoop_ThrowsInvalidOperationException()
    {
        var jobRefs = new[] { "A" };
        var edges = new[] { ("A", "A") };

        Assert.Throws<InvalidOperationException>(() => PrecedenceGraph.TopologicalSort(jobRefs, edges));
    }

    [Fact]
    public void TopologicalSort_EdgeReferencesUnknownJob_ThrowsInvalidOperationException()
    {
        var jobRefs = new[] { "A", "B" };
        var edges = new[] { ("A", "ZZZ") };

        Assert.Throws<InvalidOperationException>(() => PrecedenceGraph.TopologicalSort(jobRefs, edges));
    }
}
