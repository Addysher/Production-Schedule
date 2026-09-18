using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.ConstraintResolution;
using Xunit;

namespace ProductionScheduler.Tests.ConstraintResolution;

public class SetupResolverTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GetSetupMinutesAsync_ReturnsConfiguredDuration_WhenRowExists()
    {
        using var db = CreateContext();
        db.SetupTimes.Add(new SetupTime { FromComponentType = "WallFrames", ToComponentType = "Trusses", DurationMins = 45 });
        await db.SaveChangesAsync();

        var resolver = new SetupResolver(db);
        var minutes = await resolver.GetSetupMinutesAsync("WallFrames", "Trusses");

        Assert.Equal(45, minutes);
    }

    [Fact]
    public async Task GetSetupMinutesAsync_ReturnsZero_WhenNoRowFound()
    {
        using var db = CreateContext();

        var resolver = new SetupResolver(db);
        var minutes = await resolver.GetSetupMinutesAsync("WallFrames", "Trusses");

        Assert.Equal(0, minutes);
    }

    [Fact]
    public async Task GetSetupMinutesAsync_IsDirectionSensitive()
    {
        using var db = CreateContext();
        db.SetupTimes.Add(new SetupTime { FromComponentType = "WallFrames", ToComponentType = "Trusses", DurationMins = 45 });
        await db.SaveChangesAsync();

        var resolver = new SetupResolver(db);
        var reverseMinutes = await resolver.GetSetupMinutesAsync("Trusses", "WallFrames");

        Assert.Equal(0, reverseMinutes);
    }
}
