using ProductionScheduler.Scheduling.Engine;
using Xunit;

namespace ProductionScheduler.Tests.Engine;

public class MachineAssignerTests
{
    private static readonly MachineAssignerStation[] Stations =
    {
        new(1, "WallFrames", true),
        new(2, "WallFrames", true),
        new(3, "Trusses", true),
        new(4, "WallFrames", false) // inactive — must never receive work
    };

    [Fact]
    public void Assign_FlexibleJob_GoesToCompatibleStationWithLowestLoad()
    {
        var jobs = new[]
        {
            new MachineAssignerJob("J1", "WallFrames", 100, false, null),
            new MachineAssignerJob("J2", "WallFrames", 50, false, null)
        };

        var assignments = MachineAssigner.Assign(jobs, Stations);

        // J1 goes first (arbitrary tie between station 1 and 2, both load 0) -> station 1 (lowest id on tie).
        Assert.Equal(1, assignments.Single(a => a.JobRef == "J1").StationId);
        // J2 should now prefer station 2, since station 1 carries J1's 100 minutes of load.
        Assert.Equal(2, assignments.Single(a => a.JobRef == "J2").StationId);
    }

    [Fact]
    public void Assign_ManuallyScheduledJob_IsLockedToItsManualStation_EvenIfNotLowestLoad()
    {
        var jobs = new[]
        {
            new MachineAssignerJob("Pinned", "WallFrames", 500, true, 2),
            new MachineAssignerJob("Flexible", "WallFrames", 10, false, null)
        };

        var assignments = MachineAssigner.Assign(jobs, Stations);

        Assert.Equal(2, assignments.Single(a => a.JobRef == "Pinned").StationId);
        // Flexible job should avoid station 2 (now loaded 500) and go to station 1.
        Assert.Equal(1, assignments.Single(a => a.JobRef == "Flexible").StationId);
    }

    [Fact]
    public void Assign_NeverRoutesToAStationOfADifferentComponentType()
    {
        var jobs = new[] { new MachineAssignerJob("Truss1", "Trusses", 20, false, null) };

        var assignments = MachineAssigner.Assign(jobs, Stations);

        Assert.Equal(3, assignments.Single().StationId);
    }

    [Fact]
    public void Assign_NeverRoutesToAnInactiveStation()
    {
        var jobs = Enumerable.Range(1, 5)
            .Select(i => new MachineAssignerJob($"J{i}", "WallFrames", 10, false, null))
            .ToList();

        var assignments = MachineAssigner.Assign(jobs, Stations);

        Assert.All(assignments, a => Assert.NotEqual(4, a.StationId));
    }

    [Fact]
    public void Assign_NoCompatibleActiveStation_Throws()
    {
        var jobs = new[] { new MachineAssignerJob("Orphan", "Roofing", 10, false, null) };

        Assert.Throws<InvalidOperationException>(() => MachineAssigner.Assign(jobs, Stations));
    }

    [Fact]
    public void Assign_ManuallyScheduledJobMissingManualStationId_Throws()
    {
        var jobs = new[] { new MachineAssignerJob("Bad", "WallFrames", 10, true, null) };

        Assert.Throws<InvalidOperationException>(() => MachineAssigner.Assign(jobs, Stations));
    }
}
