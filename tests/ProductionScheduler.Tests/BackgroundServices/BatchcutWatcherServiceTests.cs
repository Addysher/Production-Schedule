using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

public class BatchcutWatcherServiceTests
{
    [Theory]
    [InlineData("59778MWF__DONE", "59778MWF")]
    [InlineData("12345TRF__DONE", "12345TRF")]
    public void ResolveJobRef_StripsDoneSuffixAndTrailingUnderscore(string folderName, string expectedJobRef)
    {
        Assert.Equal(expectedJobRef, BatchcutWatcherService.ResolveJobRef(folderName));
    }
}
