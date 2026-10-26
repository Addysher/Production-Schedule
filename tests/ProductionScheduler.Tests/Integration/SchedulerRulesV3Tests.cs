using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// v3.0 scheduler rules (§3.3), as the factory plans by hand: called up by the booked delivery date; TBA jobs
// pencilled in; finished within the delivery window; the first line takes the work before sharing; cut jobs
// get no priority; a line's speed is its EqA per hour.
[Collection(IntegrationCollection.Name)]
public class SchedulerRulesV3Tests
{
    private readonly IntegrationFixture _f;

    public SchedulerRulesV3Tests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string comp, decimal eqa, DateOnly? booked, DateOnly? rough = null, string status = "ready") => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = status, TotalEqA = eqa,
        BookedDeliveryDate = booked, EstBookedDeliveryDate = rough ?? booked, CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private Task AddJobsAsync(params Job[] jobs) => _f.WithDbAsync(async db => { db.Jobs.AddRange(jobs); await db.SaveChangesAsync(); });

    private static async Task<JsonElement?> ItemAsync(HttpClient admin, string jobRef) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current")).EnumerateArray()
            .Cast<JsonElement?>().FirstOrDefault(i => i!.Value.GetProperty("jobRef").GetString() == jobRef);

    private static DateTime Start(JsonElement? item) => item!.Value.GetProperty("plannedStart").GetDateTime();
    private static DateTime End(JsonElement? item) => item!.Value.GetProperty("plannedEnd").GetDateTime();
    private static int Line(JsonElement? item) => item!.Value.GetProperty("stationId").GetInt32();

    [Fact]
    public async Task ALinesSpeed_IsItsEqaPerHour()
    {
        await _f.AddStationAsync("V3 Rate Line", "V3Rate", eqaPerHour: 10);
        await AddJobsAsync(NewJob("V3-RATE", "V3Rate", 100, SiteClock.Today.AddDays(30)));
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);

        var item = await ItemAsync(admin, "V3-RATE");
        Assert.Equal(10, (End(item) - Start(item)).TotalHours, 2); // 100 Eq at 10 Eq/h, on the 24 h test calendar
    }

    [Fact]
    public async Task BookedJobsAreCalledUp_TbaJobsArePencilledIn_AndPencillingCanBeSwitchedOff()
    {
        await _f.AddStationAsync("V3 Tba Line", "V3Tba");
        await AddJobsAsync(
            NewJob("V3-BOOKED", "V3Tba", 60, SiteClock.Today.AddDays(10)),
            NewJob("V3-TBA", "V3Tba", 60, booked: null, rough: SiteClock.Today.AddDays(5)));
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);

        var tba = await ItemAsync(admin, "V3-TBA");
        Assert.True(tba!.Value.GetProperty("isTba").GetBoolean());
        Assert.False((await ItemAsync(admin, "V3-BOOKED"))!.Value.GetProperty("isTba").GetBoolean());
        Assert.True(Start(tba) < Start(await ItemAsync(admin, "V3-BOOKED"))); // pencilled by its (earlier) rough date, holding its slot

        // A TBA job can't be cut until it's booked.
        var approve = await admin.PostAsync("/api/jobs/V3-TBA/approve-for-cutting", null);
        Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
        Assert.Contains("TBA", await approve.Content.ReadAsStringAsync());

        await _f.SetSettingsAsync(("PencilTbaJobs", "false"));
        try
        {
            await admin.PostAsync("/api/schedule/generate", null);
            Assert.Null(await ItemAsync(admin, "V3-TBA"));
            Assert.NotNull(await ItemAsync(admin, "V3-BOOKED"));
        }
        finally
        {
            await _f.SetSettingsAsync(("PencilTbaJobs", "true"));
        }
    }

    [Fact]
    public async Task AJob_IsMadeWithinItsDeliveryWindow()
    {
        await _f.AddStationAsync("V3 Window Line", "V3Window");
        var due = SiteClock.Today.AddDays(20);
        await AddJobsAsync(NewJob("V3-WIN", "V3Window", 60, due));
        await _f.SetSettingsAsync(("ProductionLeadDaysMin", "2"), ("ProductionLeadDaysMax", "5"));
        try
        {
            var admin = await _f.ClientAsync();
            await admin.PostAsync("/api/schedule/generate", null);

            var item = await ItemAsync(admin, "V3-WIN");
            // The test calendar works every day, so working days = calendar days here.
            Assert.True(Start(item) >= due.AddDays(-5).ToDateTime(TimeOnly.MinValue), $"started {Start(item):O}, too early for a {due} delivery");
            Assert.True(End(item) <= due.AddDays(-2).ToDateTime(TimeOnly.MinValue).AddDays(1), $"finished {End(item):O}, too late for a {due} delivery");
        }
        finally
        {
            await _f.SetSettingsAsync(("ProductionLeadDaysMin", "0"), ("ProductionLeadDaysMax", "0"));
        }
    }

    [Fact]
    public async Task TheFirstLineTakesTheWork_TheNextOnlyWhenTheFirstCantMakeTheDate()
    {
        var first = await _f.AddStationAsync("V3 Share 1", "V3Share", eqaPerHour: 60);
        var second = await _f.AddStationAsync("V3 Share 2", "V3Share", eqaPerHour: 60);
        await _f.WithDbAsync(async db =>
        {
            (await db.Stations.SingleAsync(s => s.Id == first)).DisplayOrder = 1;
            (await db.Stations.SingleAsync(s => s.Id == second)).DisplayOrder = 2;
            await db.SaveChangesAsync();
        });
        // Two short jobs due well ahead both fit on line 1; a long urgent one that line 1 can't also fit in time goes to line 2.
        await AddJobsAsync(
            NewJob("V3-SH-A", "V3Share", 60, SiteClock.Today.AddDays(10)),
            NewJob("V3-SH-B", "V3Share", 60, SiteClock.Today.AddDays(11)),
            NewJob("V3-SH-C", "V3Share", 1500, SiteClock.Today.AddDays(1))); // 25 h: line 1 is busy with nothing before it, so it takes it...
        await AddJobsAsync(NewJob("V3-SH-D", "V3Share", 1500, SiteClock.Today.AddDays(1))); // ...and this one can only make it on line 2
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);

        Assert.Equal(first, Line(await ItemAsync(admin, "V3-SH-C")));
        Assert.Equal(second, Line(await ItemAsync(admin, "V3-SH-D")));
        Assert.Equal(first, Line(await ItemAsync(admin, "V3-SH-A"))); // plenty of time: back on the first line
        Assert.Equal(first, Line(await ItemAsync(admin, "V3-SH-B")));
    }

    [Fact]
    public async Task ACutJob_GetsNoPriority_ProductionOrderIsByDeliveryDate()
    {
        await _f.AddStationAsync("V3 Cut Line", "V3Cut");
        await AddJobsAsync(
            NewJob("V3-CUT-LATER", "V3Cut", 60, SiteClock.Today.AddDays(20), status: "cut"),
            NewJob("V3-READY-SOONER", "V3Cut", 60, SiteClock.Today.AddDays(5)));
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);

        Assert.True(Start(await ItemAsync(admin, "V3-READY-SOONER")) < Start(await ItemAsync(admin, "V3-CUT-LATER")));
    }
}
