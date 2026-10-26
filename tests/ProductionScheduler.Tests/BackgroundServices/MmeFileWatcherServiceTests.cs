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

    // Sapphire always ends the name with a 3-character member ID, whatever the job ref looks like.
    [Theory]
    [InlineData("58749MPFP01", "58749MPF", "P01")]
    [InlineData("ABCDEFGHE17", "ABCDEFGH", "E17")]
    public void TheMemberIsAlwaysTheLastThreeCharacters(string fileName, string jobRef, string memberId)
    {
        Assert.Equal(jobRef, MmeFileWatcherService.DeriveJobRef(fileName));
        Assert.Equal(memberId, MmeFileWatcherService.DeriveMemberId(fileName));
    }
}
