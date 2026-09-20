using ProductionScheduler.Domain;
using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

public class SiteClockTests
{
    private static TimeZoneInfo Brisbane =>
        TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "E. Australia Standard Time" : "Australia/Brisbane");

    [Fact]
    public void FromUtc_ConvertsToTheSiteZone_AndCanLandOnADifferentCalendarDay()
    {
        // 20:00 UTC on the 19th is 06:00 on the 20th in Brisbane (UTC+10) — the very case where a
        // UtcNow-based "today's PRD file" would open yesterday's file at the start of the shift.
        var site = SiteClock.FromUtc(new DateTime(2026, 9, 19, 20, 0, 0, DateTimeKind.Utc), Brisbane);

        Assert.Equal(new DateTime(2026, 9, 20, 6, 0, 0), site);
        Assert.Equal(DateTimeKind.Unspecified, site.Kind); // serialises without an offset, so browsers show it as-is
    }

    [Fact]
    public void Now_IsWithinAMinuteOfTheServersLocalClock()
    {
        Assert.True((SiteClock.Now - DateTime.Now).Duration() < TimeSpan.FromMinutes(1));
    }
}

public class PrdTimestampTests
{
    private static readonly DateOnly FileDate = new(2026, 9, 20);

    [Theory]
    [InlineData("2026-09-20 10:15:30", 2026, 9, 20, 10, 15, 30)]
    [InlineData("2026-09-20T10:15:30", 2026, 9, 20, 10, 15, 30)]
    [InlineData("20/09/2026 10:15:30", 2026, 9, 20, 10, 15, 30)]
    [InlineData("5/09/2026 7:05:00", 2026, 9, 5, 7, 5, 0)]   // day-first: the 5th of September, not the 9th of May
    public void TryParseTimestamp_AcceptsTheUsualLayouts(string text, int y, int mo, int d, int h, int mi, int s)
    {
        Assert.True(PrdReaderService.TryParseTimestamp(text, FileDate, out var result));
        Assert.Equal(new DateTime(y, mo, d, h, mi, s), result);
    }

    [Fact]
    public void TryParseTimestamp_BareTime_TakesItsDateFromTheFileName()
    {
        Assert.True(PrdReaderService.TryParseTimestamp("10:15:30", FileDate, out var result));
        Assert.Equal(new DateTime(2026, 9, 20, 10, 15, 30), result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("31/02/2026 10:00:00")]
    public void TryParseTimestamp_RejectsGarbage(string text)
    {
        Assert.False(PrdReaderService.TryParseTimestamp(text, FileDate, out _));
    }
}
