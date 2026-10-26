using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Scheduler edge cases found by the functional audit: stale pins, jobs with nowhere to run, today's
// capacity, and concurrent runs.
[Collection(IntegrationCollection.Name)]
public class SchedulerEdgeCaseTests
{
    private readonly IntegrationFixture _f;

    public SchedulerEdgeCaseTests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string comp, decimal eqa, int dueInDays) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        EstBookedDeliveryDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(dueInDays), CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient c, string jobRef) =>
        (await c.GetFromJsonAsync<JsonElement>("/api/schedule/current")).EnumerateArray().Where(i => i.GetProperty("jobRef").GetString() == jobRef).ToList();

    [Fact]
    public async Task APinToAStationThatNoLongerMakesTheJobsProduct_IsIgnored()
    {
        var oldLine = await _f.AddStationAsync("EdgePin Old", "EdgePinOld");
        var newLine = await _f.AddStationAsync("EdgePin New", "EdgePinNew");
        await _f.WithDbAsync(async db =>
        {
            var job = NewJob("EDGE-PIN", "EdgePinNew", 60, 5); // product changed after it was pinned to the old line
            job.IsManuallyScheduled = true; job.ManualStationId = oldLine; job.ManualScheduleDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(2);
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);

        Assert.Equal(newLine, (await ItemsAsync(admin, "EDGE-PIN")).Single().GetProperty("stationId").GetInt32());
    }

    [Fact]
    public async Task AJobWithNowhereToRun_LosesItsOldPlan_AndShowsAsUnscheduled()
    {
        var line = await _f.AddStationAsync("EdgeOrphan Line", "EdgeOrphan");
        await _f.WithDbAsync(async db => { db.Jobs.Add(NewJob("EDGE-ORPHAN", "EdgeOrphan", 60, 5)); await db.SaveChangesAsync(); });
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);
        Assert.Single(await ItemsAsync(admin, "EDGE-ORPHAN"));

        await _f.WithDbAsync(async db => { (await db.Stations.SingleAsync(s => s.Id == line)).IsActive = false; await db.SaveChangesAsync(); });
        var run = await (await admin.PostAsync("/api/schedule/generate", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(run.GetProperty("jobsSkipped").GetInt32() >= 1);
        Assert.Empty(await ItemsAsync(admin, "EDGE-ORPHAN"));
        Assert.Null(await _f.WithDbAsync(db => db.Jobs.Where(j => j.JobRef == "EDGE-ORPHAN").Select(j => j.AssignedStationId).SingleAsync()));
    }

    [Fact]
    public async Task ConcurrentRuns_DoNotCollide()
    {
        await _f.AddStationAsync("EdgeConc Line", "EdgeConc");
        await _f.WithDbAsync(async db =>
        {
            for (var i = 0; i < 8; i++) db.Jobs.Add(NewJob($"EDGE-C{i}", "EdgeConc", 60, i + 1));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.PostAsync("/api/schedule/generate", null)));

        Assert.All(runs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        for (var i = 0; i < 8; i++) Assert.Single(await ItemsAsync(admin, $"EDGE-C{i}"));
    }
}
