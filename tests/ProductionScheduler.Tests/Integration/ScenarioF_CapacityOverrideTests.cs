using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario F (v3.0): one-off changes to a line's day. Closed (shutdown / public holiday) takes
// the day out for that line — or all lines at once — and overtime hours lengthen it.
[Collection(IntegrationCollection.Name)]
public class ScenarioF_CapacityOverrideTests
{
    private readonly IntegrationFixture _f;

    public ScenarioF_CapacityOverrideTests(IntegrationFixture fixture) => _f = fixture;

    private static Job ReadyJob(string jobRef, string comp, decimal eqa) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        EstBookedDeliveryDate = SiteClock.Today.AddDays(30), CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<List<(DateOnly Day, DateTime Start, DateTime End)>> SegmentsAsync(HttpClient admin, string jobRef)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Where(i => i.GetProperty("jobRef").GetString() == jobRef)
            .SelectMany(i => i.GetProperty("segments").EnumerateArray())
            .Select(s => (DateOnly.Parse(s.GetProperty("date").GetString()!), s.GetProperty("start").GetDateTime(), s.GetProperty("end").GetDateTime()))
            .ToList();
    }

    [Fact]
    public async Task AClosedDay_IsSkipped_AndSavingTheChangeRePlans()
    {
        const string comp = "ScnFClosed";
        var stationId = await _f.AddStationAsync("ScnF Closed Line", comp, eqaPerHour: 60);
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("F-CLOSED", comp, 3000)); await db.SaveChangesAsync(); }); // 50 h: runs through tomorrow
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);
        var tomorrow = SiteClock.Today.AddDays(1);
        Assert.Contains(await SegmentsAsync(admin, "F-CLOSED"), s => s.Day == tomorrow);

        var closed = await admin.PostAsJsonAsync($"/api/stations/{stationId}/capacity-overrides", new { overrideDate = tomorrow, isClosed = true, note = "Shutdown" });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var segments = await SegmentsAsync(admin, "F-CLOSED"); // no Recalculate: the change re-planned
        Assert.DoesNotContain(segments, s => s.Day == tomorrow);
        Assert.Contains(segments, s => s.Day > tomorrow);
        var line = (await admin.GetFromJsonAsync<JsonElement>("/api/stations")).EnumerateArray().Single(s => s.GetProperty("id").GetInt32() == stationId);
        Assert.Equal(1440m, line.GetProperty("todayTargetEqa").GetDecimal()); // 60 Eq/h × 24 h today
    }

    [Fact]
    public async Task APublicHoliday_ClosesEveryLine()
    {
        var lineA = await _f.AddStationAsync("ScnF Holiday A", "ScnFHolA");
        var lineB = await _f.AddStationAsync("ScnF Holiday B", "ScnFHolB");
        var day = SiteClock.Today.AddDays(5);
        var admin = await _f.ClientAsync();

        var response = await admin.PostAsJsonAsync("/api/stations/capacity-overrides/all-lines", new { overrideDate = day, isClosed = true, note = "Public holiday" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        foreach (var id in new[] { lineA, lineB })
        {
            var changes = await admin.GetFromJsonAsync<JsonElement>($"/api/stations/{id}/capacity-overrides");
            Assert.Contains(changes.EnumerateArray(), c => c.GetProperty("overrideDate").GetString() == day.ToString("yyyy-MM-dd") && c.GetProperty("isClosed").GetBoolean());
        }

        // Tidy up: other tests share these days.
        await _f.WithDbAsync(db => db.StationCapacityOverrides.Where(o => o.OverrideDate == day).ExecuteDeleteAsync());
    }

    [Fact]
    public async Task Overtime_LengthensTheLinesDay()
    {
        const string comp = "ScnFOvertime";
        var stationId = await _f.AddStationAsync("ScnF Overtime Line", comp, eqaPerHour: 60);
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("F-OT", comp, 1800)); await db.SaveChangesAsync(); }); // 30 h
        var admin = await _f.ClientAsync();
        var shift = await admin.PostAsJsonAsync("/api/shifts", new { name = "OT day", startTime = "06:00:00", endTime = "14:00:00", isActive = true, breaks = Array.Empty<object>() });
        var shiftId = (await shift.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var tomorrow = SiteClock.Today.AddDays(1);
        try
        {
            Assert.True((await SegmentsAsync(admin, "F-OT")).Where(s => s.Day == tomorrow).All(s => s.End.TimeOfDay <= TimeSpan.FromHours(14)));

            var overtime = await admin.PostAsJsonAsync($"/api/stations/{stationId}/capacity-overrides",
                new { overrideDate = tomorrow, isClosed = false, startTime = "06:00:00", endTime = "18:00:00", note = "Overtime" });
            Assert.Equal(HttpStatusCode.OK, overtime.StatusCode);

            Assert.Contains(await SegmentsAsync(admin, "F-OT"), s => s.Day == tomorrow && s.End.TimeOfDay > TimeSpan.FromHours(14));
        }
        finally
        {
            await admin.DeleteAsync($"/api/shifts/{shiftId}");
        }
    }

    [Fact]
    public async Task AChangeNeedsHoursOrClosed()
    {
        var stationId = await _f.AddStationAsync("ScnF Bad Change", "ScnFBad");
        var admin = await _f.ClientAsync();

        var response = await admin.PostAsJsonAsync($"/api/stations/{stationId}/capacity-overrides", new { overrideDate = SiteClock.Today.AddDays(2), isClosed = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

// §7 Step 1 — "Seed Settings with default values from §6.2; seed one default Shift".
[Collection(IntegrationCollection.Name)]
public class SeedDataTests
{
    private readonly IntegrationFixture _f;

    public SeedDataTests(IntegrationFixture fixture) => _f = fixture;

    [Fact]
    public async Task Startup_SeedsEveryDocumentedSettingKey_AndADefaultShift()
    {
        var settings = await _f.WithDbAsync(db => db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value));
        foreach (var key in new[]
                 {
                     "AtRiskBufferMinutes", "ShiftStartHour", "ShiftEndHour", "WorkingDays", "ScheduleRetentionMonths",
                     "PrdPollIntervalMinutes", "BatchcutPollIntervalSeconds", "RawFilesPath", "SawFilesDoNotCutPath",
                     "SawFilesForCuttingPath", "SawFilesDoneBasePath", "MmePollIntervalSeconds", "CreepThresholdPercent",
                     "MpsPreMoveDaysBeforeProduction",
                 })
        {
            Assert.True(settings.ContainsKey(key), $"Settings should contain '{key}'");
        }

        Assert.Equal("5", settings["PrdPollIntervalMinutes"]);
        Assert.Equal("20", settings["CreepThresholdPercent"]);
        Assert.Equal("300", settings["MmePollIntervalSeconds"]);

        Assert.True(await _f.WithDbAsync(db => db.Shifts.AnyAsync()), "a default shift should exist");
    }
}
