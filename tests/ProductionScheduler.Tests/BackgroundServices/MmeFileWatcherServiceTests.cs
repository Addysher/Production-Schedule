using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

public class MmeFileWatcherServiceTests
{
    [Theory]
    [InlineData("59778MWFE17", "59778MWF")]
    [InlineData("59778MWFI31", "59778MWF")]
    [InlineData("59778MWFE11", "59778MWF")]
    public void DeriveJobRef_StripsLastThreeCharacters(string fileName, string expectedJobRef)
    {
        Assert.Equal(expectedJobRef, MmeFileWatcherService.DeriveJobRef(fileName));
    }

    [Fact]
    public void DeriveJobRef_TooShortToContainAMemberId_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, MmeFileWatcherService.DeriveJobRef("E17"));
    }

    // Real floor designs have 4-character members (P05A), so the member isn't always the last 3 characters.
    [Theory]
    [InlineData("58749MPFP05A", "58749MPF")] // job not imported yet: the job-ref shape decides
    [InlineData("58749MPFP01", "58749MPF")]
    [InlineData("59778MWFE17", "59778MWF")]
    public void ResolveJobRef_UsesTheJobRefShape_WhenTheJobIsntKnownYet(string fileName, string expected)
    {
        Assert.Equal(expected, MmeFileWatcherService.ResolveJobRef(fileName, []));
    }

    [Fact]
    public void ResolveJobRef_PrefersAKnownJob_AndTheMemberIsWhateverFollowsIt()
    {
        string[] known = ["TESTJOB", "TESTJOB2"];

        Assert.Equal("TESTJOB2", MmeFileWatcherService.ResolveJobRef("TESTJOB2P05A", known));
        Assert.Equal("P05A", MmeFileWatcherService.MemberIdOf("TESTJOB2P05A", "TESTJOB2"));
    }
}
