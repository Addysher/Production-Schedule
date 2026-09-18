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
}
