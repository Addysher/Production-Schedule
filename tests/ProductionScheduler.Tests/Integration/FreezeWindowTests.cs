using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// v3.0 freeze (lock-out) window: not-started jobs planned inside today + FreezeWindowDays working
// days keep their slot. The test calendar runs 24/7, so every day is a working day here.
[Collection(IntegrationCollection.Name)]
public class FreezeWindowTests
{
    private readonly IntegrationFixture _f;

    public FreezeWindowTests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string comp, decimal eqa, DateOnly due) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        ExpectedDeliveryDate = due, EstBookedDeliveryDate = due, CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<JsonElement> ItemAsync(HttpClient client, string jobRef) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/schedule/current")).EnumerateArray().Single(i => i.GetProperty("jobRef").GetString() == jobRef);

    private async Task WithFreezeAsync(int days, Func<Task> body)
    {
        await _f.SetSettingsAsync(("FreezeWindowDays", days.ToString()));
        try
        {
            await body();
        }
        finally
        {
            await _f.SetSettingsAsync(("FreezeWindowDays", "0"));
        }
    }

    [Fact]
    public async Task FrozenJob_KeepsItsSlot_AndAMoreUrgentJobGoesAfterIt()
    {
        const string comp = "FrzKeep";
        await _f.AddStationAsync("FrzKeep Line", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db => { db.Jobs.Add(NewJob("FRZ-A", comp, 120, today.AddDays(20))); await db.SaveChangesAsync(); });
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);
        var before = await ItemAsync(admin, "FRZ-A");

        await WithFreezeAsync(2, async () =>
        {
            // An urgent job arrives: without the freeze, EDD would put it ahead of FRZ-A.
            await _f.WithDbAsync(async db => { db.Jobs.Add(NewJob("FRZ-URGENT", comp, 60, today)); await db.SaveChangesAsync(); });
            var run = await (await admin.PostAsync("/api/schedule/generate", null)).Content.ReadFromJsonAsync<JsonElement>();

            var after = await ItemAsync(admin, "FRZ-A");
            Assert.Equal(before.GetProperty("plannedStart").GetDateTime(), after.GetProperty("plannedStart").GetDateTime());
            Assert.Equal(before.GetProperty("id").GetInt32(), after.GetProperty("id").GetInt32()); // the same plan row, untouched
            Assert.True(after.GetProperty("isFrozen").GetBoolean());
            Assert.True(run.GetProperty("jobsFrozen").GetInt32() >= 1);

            var urgent = await ItemAsync(admin, "FRZ-URGENT");
            Assert.True(urgent.GetProperty("plannedStart").GetDateTime() >= after.GetProperty("plannedEnd").GetDateTime().AddSeconds(-1));
        });
    }

    [Fact]
    public async Task MovingAFrozenJob_OrDroppingIntoTheWindow_IsRefused_ButALaterDayIsFine()
    {
        const string comp = "FrzMove";
        var station = await _f.AddStationAsync("FrzMove Line", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(NewJob("FRZ-NEAR", comp, 60, today.AddDays(1)));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null); // FRZ-NEAR planned from now

        await WithFreezeAsync(2, async () =>
        {
            var moveFrozen = await admin.PostAsJsonAsync("/api/jobs/FRZ-NEAR/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(10) });
            Assert.Equal(HttpStatusCode.BadRequest, moveFrozen.StatusCode);
            Assert.Contains("freeze window", await moveFrozen.Content.ReadAsStringAsync());

            // A job planned beyond the window can't be dropped into it, but can move to a later day.
            await _f.WithDbAsync(async db => { db.Jobs.Add(NewJob("FRZ-FAR", comp, 60, today.AddDays(30))); await db.SaveChangesAsync(); });
            await admin.PostAsJsonAsync("/api/jobs/FRZ-FAR/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(8) }); // park it outside first
            var intoWindow = await admin.PostAsJsonAsync("/api/jobs/FRZ-FAR/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(1) });
            Assert.Equal(HttpStatusCode.BadRequest, intoWindow.StatusCode);

            var later = await admin.PostAsJsonAsync("/api/jobs/FRZ-FAR/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(5) });
            Assert.Equal(HttpStatusCode.OK, later.StatusCode);
        });
    }

    [Fact]
    public async Task APlanThatEndedWithoutTheJobStarting_IsNotFrozen_AndIsReplannedFromNow()
    {
        const string comp = "FrzStale";
        var station = await _f.AddStationAsync("FrzStale Line", comp);
        var yesterday = SiteClock.Now.AddDays(-1);
        await _f.WithDbAsync(async db =>
        {
            var job = NewJob("FRZ-STALE", comp, 60, DateOnly.FromDateTime(SiteClock.Now).AddDays(3));
            job.AssignedStationId = station;
            db.Jobs.Add(job);
            db.ScheduleItems.Add(new ScheduleItem
            {
                JobRef = "FRZ-STALE", StationId = station, ScheduleDate = DateOnly.FromDateTime(yesterday),
                PlannedStart = yesterday, PlannedEnd = yesterday.AddHours(1), PlannedEqA = 60, GeneratedAt = yesterday,
            });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        await WithFreezeAsync(2, async () =>
        {
            await admin.PostAsync("/api/schedule/generate", null);
            var item = await ItemAsync(admin, "FRZ-STALE");
            Assert.True(item.GetProperty("plannedStart").GetDateTime() >= SiteClock.Now.AddMinutes(-1), "re-planned from now, not left in the past");
        });
    }

    [Fact]
    public async Task CalendarSettings_ReportsWhereTheWindowEnds_AndZeroTurnsItOff()
    {
        var admin = await _f.ClientAsync();
        var today = SiteClock.Now.Date;

        await WithFreezeAsync(2, async () =>
        {
            var settings = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/calendar-settings");
            Assert.Equal(today.AddDays(3), settings.GetProperty("freezeUntil").GetDateTime()); // through the end of day +2
        });

        var off = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/calendar-settings");
        Assert.Equal(JsonValueKind.Null, off.GetProperty("freezeUntil").ValueKind);
    }
}
