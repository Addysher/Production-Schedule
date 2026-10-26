using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Settings drive the scheduler and the background services: values that would hang scheduling or
// stop a service are refused when saved, and anything bad that still reaches the database falls back.
[Collection(IntegrationCollection.Name)]
public class SettingsValidationTests
{
    private readonly IntegrationFixture _f;

    public SettingsValidationTests(IntegrationFixture fixture) => _f = fixture;

    [Theory]
    [InlineData("WorkingDays", "")]
    [InlineData("WorkingDays", "1,2,9")]
    [InlineData("ShiftStartHour", "25")]
    [InlineData("ShiftEndHour", "-1")]
    [InlineData("PrdPollIntervalMinutes", "0")]
    [InlineData("BatchcutPollIntervalSeconds", "abc")]
    [InlineData("ThemeMode", "purple")]
    [InlineData("ThemePrimaryColor", "teal")]
    public async Task UnusableValues_AreRefused_WithAReason(string key, string value)
    {
        var admin = await _f.ClientAsync();
        var response = await admin.PutAsJsonAsync($"/api/settings/{key}", new { value });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(await response.Content.ReadAsStringAsync()), "the admin is told why");
    }

    [Fact]
    public async Task AShiftThatEndsBeforeItStarts_IsRefused()
    {
        var admin = await _f.ClientAsync(); // the test calendar runs 0-24
        var response = await admin.PutAsJsonAsync("/api/settings/ShiftEndHour", new { value = "0" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AFolderPath_CanBeClearedBackToEmpty()
    {
        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/RawFilesPath", new { value = "C:\\somewhere" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/RawFilesPath", new { value = "" })).StatusCode);
    }

    [Fact]
    public async Task BadCalendarValuesInTheDatabase_DoNotHangScheduling()
    {
        var admin = await _f.ClientAsync();
        await _f.SetSettingsAsync(("WorkingDays", ""), ("ShiftStartHour", "20"), ("ShiftEndHour", "4"));
        try
        {
            var run = admin.PostAsync("/api/schedule/generate", null);
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.Same(run, finished);
            Assert.Equal(HttpStatusCode.OK, (await run).StatusCode);
        }
        finally
        {
            await _f.SetSettingsAsync(("WorkingDays", "0,1,2,3,4,5,6"), ("ShiftStartHour", "0"), ("ShiftEndHour", "24"));
        }
    }
}
