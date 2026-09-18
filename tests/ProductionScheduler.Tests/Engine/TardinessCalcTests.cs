using ProductionScheduler.Scheduling.Engine;
using Xunit;

namespace ProductionScheduler.Tests.Engine;

public class TardinessCalcTests
{
    private static readonly DateTime Due = new(2026, 9, 21, 12, 0, 0);

    [Fact]
    public void TotalWeightedTardinessMinutes_SumsOnlyLateJobs()
    {
        var jobs = new[]
        {
            new TardinessJob("OnTime", Due, Due.AddMinutes(-30)),
            new TardinessJob("Late30", Due, Due.AddMinutes(30)),
            new TardinessJob("Late90", Due, Due.AddMinutes(90))
        };

        var total = TardinessCalc.TotalWeightedTardinessMinutes(jobs);

        Assert.Equal(120m, total);
    }

    [Fact]
    public void TotalWeightedTardinessMinutes_AppliesPerJobWeight()
    {
        var jobs = new[]
        {
            new TardinessJob("HighPriorityLate", Due, Due.AddMinutes(60), Weight: 3m),
            new TardinessJob("LowPriorityLate", Due, Due.AddMinutes(60), Weight: 1m)
        };

        var total = TardinessCalc.TotalWeightedTardinessMinutes(jobs);

        Assert.Equal(240m, total); // (3*60) + (1*60)
    }

    [Fact]
    public void MaxTardinessMinutes_ReturnsWorstSingleJob()
    {
        var jobs = new[]
        {
            new TardinessJob("Late30", Due, Due.AddMinutes(30)),
            new TardinessJob("Late90", Due, Due.AddMinutes(90)),
            new TardinessJob("OnTime", Due, Due.AddMinutes(-10))
        };

        Assert.Equal(90, TardinessCalc.MaxTardinessMinutes(jobs));
    }

    [Fact]
    public void MaxTardinessMinutes_EmptySet_ReturnsZero()
    {
        Assert.Equal(0, TardinessCalc.MaxTardinessMinutes(Array.Empty<TardinessJob>()));
    }

    [Fact]
    public void LateJobCount_CountsOnlyJobsPastDueDate()
    {
        var jobs = new[]
        {
            new TardinessJob("OnTime", Due, Due),
            new TardinessJob("Late", Due, Due.AddMinutes(1)),
            new TardinessJob("Early", Due, Due.AddMinutes(-1))
        };

        Assert.Equal(1, TardinessCalc.LateJobCount(jobs));
    }
}
