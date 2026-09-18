using ProductionScheduler.Scheduling.Engine;
using Xunit;

namespace ProductionScheduler.Tests.Engine;

public class SequencerTests
{
    private static readonly DateTime ScheduleStart = new(2026, 9, 21, 6, 0, 0);

    [Fact]
    public void SequenceEdd_OrdersByDueDateAscending()
    {
        var jobs = new[]
        {
            new SequencerJob("C", "WallFrames", ScheduleStart.AddDays(3), 60),
            new SequencerJob("A", "WallFrames", ScheduleStart.AddDays(1), 60),
            new SequencerJob("B", "WallFrames", ScheduleStart.AddDays(2), 60)
        };

        var sequence = Sequencer.SequenceEdd(jobs);

        Assert.Equal(new[] { "A", "B", "C" }, sequence);
    }

    [Fact]
    public void SequenceEdd_TenPlusJobs_IsDeterministicAndProducesAMeasurableTardinessScore()
    {
        // 12 jobs, single station/component type so setup time is irrelevant to this scenario —
        // this isolates EDD's ordering behaviour per the §7 Step 4 exit condition.
        var rng = new Random(42);
        var jobs = Enumerable.Range(1, 12)
            .Select(i => new SequencerJob(
                $"J{i}",
                "WallFrames",
                ScheduleStart.AddMinutes(rng.Next(60, 600)),
                ProcessingMinutes: 45))
            .ToList();

        var firstRun = Sequencer.SequenceEdd(jobs);
        var secondRun = Sequencer.SequenceEdd(jobs);

        Assert.Equal(12, firstRun.Count);
        Assert.Equal(firstRun, secondRun); // deterministic

        var jobsByRef = jobs.ToDictionary(j => j.JobRef);
        var orderedDueDates = firstRun.Select(r => jobsByRef[r].DueDate).ToList();
        Assert.Equal(orderedDueDates.OrderBy(d => d), orderedDueDates); // correctly ordered

        var completions = SimulateSequentialCompletion(firstRun, jobsByRef, (_, _) => 0, ScheduleStart);
        var tardinessJobs = completions.Select(c => new TardinessJob(c.JobRef, jobsByRef[c.JobRef].DueDate, c.CompletionTime));
        var totalTardiness = TardinessCalc.TotalWeightedTardinessMinutes(tardinessJobs);

        Assert.True(totalTardiness >= 0); // score is measurable — concrete value asserted below
        Assert.Equal(482m, totalTardiness);
    }

    [Fact]
    public void SequenceAtcs_ReducesTardiness_WhenSetupTimesAreSignificant()
    {
        // Two component types with a costly 30-minute changeover between them and no cost
        // switching within the same type. Strict EDD interleaves A/B, paying the changeover
        // penalty three times; ATCS should group same-type jobs together and finish on time.
        var jobs = new[]
        {
            new SequencerJob("A1", "TypeA", ScheduleStart.AddMinutes(100), 20),
            new SequencerJob("B1", "TypeB", ScheduleStart.AddMinutes(110), 20),
            new SequencerJob("A2", "TypeA", ScheduleStart.AddMinutes(120), 20),
            new SequencerJob("B2", "TypeB", ScheduleStart.AddMinutes(130), 20)
        };

        int Setup(string from, string to) => from == to ? 0 : 30;

        var eddSequence = Sequencer.SequenceEdd(jobs);
        var atcsSequence = Sequencer.SequenceAtcs(jobs, Setup, ScheduleStart);

        Assert.Equal(new[] { "A1", "B1", "A2", "B2" }, eddSequence);
        Assert.Equal(new[] { "A1", "A2", "B1", "B2" }, atcsSequence); // groups by type to dodge changeovers

        var jobsByRef = jobs.ToDictionary(j => j.JobRef);

        var eddTardiness = TotalTardinessFor(eddSequence, jobsByRef, Setup);
        var atcsTardiness = TotalTardinessFor(atcsSequence, jobsByRef, Setup);

        Assert.Equal(40m, eddTardiness);
        Assert.Equal(0m, atcsTardiness);
        Assert.True(atcsTardiness < eddTardiness);
    }

    private static decimal TotalTardinessFor(
        IReadOnlyList<string> sequence,
        IReadOnlyDictionary<string, SequencerJob> jobsByRef,
        Func<string, string, int> setupMinutes)
    {
        var completions = SimulateSequentialCompletion(sequence, jobsByRef, setupMinutes, ScheduleStart);
        var tardinessJobs = completions.Select(c => new TardinessJob(c.JobRef, jobsByRef[c.JobRef].DueDate, c.CompletionTime));
        return TardinessCalc.TotalWeightedTardinessMinutes(tardinessJobs);
    }

    // Test-only helper: replays a sequence through a single machine to derive completion times,
    // charging a changeover between consecutive jobs of different component types.
    private static List<(string JobRef, DateTime CompletionTime)> SimulateSequentialCompletion(
        IReadOnlyList<string> sequence,
        IReadOnlyDictionary<string, SequencerJob> jobsByRef,
        Func<string, string, int> setupMinutes,
        DateTime start)
    {
        var results = new List<(string, DateTime)>();
        var t = start;
        string? currentType = null;

        foreach (var jobRef in sequence)
        {
            var job = jobsByRef[jobRef];
            var setup = currentType is null ? 0 : setupMinutes(currentType, job.ComponentType);
            t = t.AddMinutes(setup + (double)job.ProcessingMinutes);
            results.Add((jobRef, t));
            currentType = job.ComponentType;
        }

        return results;
    }
}
