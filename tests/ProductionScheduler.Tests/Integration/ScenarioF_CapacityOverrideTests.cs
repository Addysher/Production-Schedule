using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario F: a StationCapacityOverride for tomorrow replaces the station's
// DailyEqATarget when the scheduler plans that day.
[Collection(IntegrationCollection.Name)]
public class ScenarioF_CapacityOverrideTests
{
    private readonly IntegrationFixture _f;

    public ScenarioF_CapacityOverrideTests(IntegrationFixture fixture) => _f = fixture;

    private async Task<DateTime> PlannedEndAsync(HttpClient admin, string jobRef)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Single(i => i.GetProperty("jobRef").GetString() == jobRef).GetProperty("plannedEnd").GetDateTime();
    }

    [Fact]
    public async Task OverrideForTomorrow_ReplacesTheDailyTarget_WhenPlanningThatDay()
    {
        const string comp = "ScnF";
        // 1 minute of work per EqA (no job type configured), 24h days: only the EqA cap can spread a job over days.
        var stationId = await _f.AddStationAsync("ScnF Station", comp, dailyEqATarget: 100);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(new Job
            {
                JobRef = "F1", ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = 300,
                EstBookedDeliveryDate = today.AddDays(30), ExpectedDeliveryDate = today.AddDays(30),
                CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
            });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        // Control: 300 EqA at a 100/day cap cannot finish before the third calendar day.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);
        var controlEnd = await PlannedEndAsync(admin, "F1");
        Assert.True(DateOnly.FromDateTime(controlEnd) >= today.AddDays(2), $"with a 100/day target F1 should end on day 3+, ended {controlEnd:O}");

        // Override tomorrow to 400 EqA: the remainder now fits entirely in tomorrow.
        var tomorrow = today.AddDays(1);
        var create = await admin.PostAsJsonAsync($"/api/stations/{stationId}/capacity-overrides", new { overrideDate = tomorrow, eqATarget = 400 });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        var overriddenEnd = await PlannedEndAsync(admin, "F1");
        Assert.Equal(tomorrow, DateOnly.FromDateTime(overriddenEnd));
    }

    [Fact]
    public async Task StationWithNoTarget_IsNotCapacityLimited()
    {
        const string comp = "ScnFNoCap";
        await _f.AddStationAsync("ScnFNoCap Station", comp, dailyEqATarget: null);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(new Job
            {
                JobRef = "FN1", ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = 300,
                EstBookedDeliveryDate = today.AddDays(30), ExpectedDeliveryDate = today.AddDays(30),
                CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
            });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        // 300 minutes of work in a 24h day: finishes within ~5 hours of now, never more than a day out.
        Assert.True(await PlannedEndAsync(admin, "FN1") <= SiteClock.Now.AddMinutes(305));
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
