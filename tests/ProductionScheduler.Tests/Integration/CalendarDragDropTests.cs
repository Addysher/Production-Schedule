using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Calendar drag-and-drop (Schedule manually under the hood): a dropped job starts on the day it was
// dropped, the station's other work flows around it without overlapping, and only work that hasn't
// started production can be moved. Admins and Supervisors can move jobs.
[Collection(IntegrationCollection.Name)]
public class CalendarDragDropTests
{
    private readonly IntegrationFixture _f;

    public CalendarDragDropTests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string comp, decimal eqa, DateOnly due, string status = "ready", int? stationId = null) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = status, TotalEqA = eqa,
        ExpectedDeliveryDate = due, EstBookedDeliveryDate = due, AssignedStationId = stationId,
        CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient client, string prefix) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/schedule/current"))
            .EnumerateArray().Where(i => i.GetProperty("jobRef").GetString()!.StartsWith(prefix)).ToList();

    private static DateTime Dt(JsonElement e, string name) => e.GetProperty(name).GetDateTime();

    [Fact]
    public async Task DroppedOntoABusyDay_StartsThatDay_AndTheRestOfTheStationFlowsAroundItWithoutOverlap()
    {
        const string comp = "DndBusy";
        var station = await _f.AddStationAsync("DndBusy Line", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        var pinDate = today.AddDays(1);

        await _f.WithDbAsync(async db =>
        {
            // ~60 hours of work from now (1 min per EqA in the test calendar), so tomorrow is busy.
            for (var i = 0; i < 6; i++) db.Jobs.Add(NewJob($"DNDB-{i}", comp, 600, today.AddDays(1 + i)));
            db.Jobs.Add(NewJob("DNDB-DROP", comp, 300, today.AddDays(30))); // due last: EDD would put it at the end
            await db.SaveChangesAsync();
        });

        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        var drop = await admin.PostAsJsonAsync("/api/jobs/DNDB-DROP/schedule-manually", new { stationId = station, scheduleDate = pinDate });
        Assert.Equal(HttpStatusCode.OK, drop.StatusCode);

        var items = (await ItemsAsync(admin, "DNDB-")).OrderBy(i => Dt(i, "plannedStart")).ToList();
        Assert.Equal(7, items.Count);

        var dropped = items.Single(i => i.GetProperty("jobRef").GetString() == "DNDB-DROP");
        Assert.Equal(pinDate, DateOnly.FromDateTime(Dt(dropped, "plannedStart")));
        // Not smeared across days by other jobs having used up the station's time.
        Assert.Equal(300, (Dt(dropped, "plannedEnd") - Dt(dropped, "plannedStart")).TotalMinutes, 1);

        for (var k = 1; k < items.Count; k++)
            Assert.True(Dt(items[k], "plannedStart") >= Dt(items[k - 1], "plannedEnd").AddSeconds(-1),
                $"{items[k].GetProperty("jobRef")} starts before {items[k - 1].GetProperty("jobRef")} ends");

        // The job's own dates follow the move.
        var job = await admin.GetFromJsonAsync<JsonElement>("/api/jobs");
        var summary = job.EnumerateArray().Single(j => j.GetProperty("jobRef").GetString() == "DNDB-DROP");
        Assert.Equal(pinDate, DateOnly.FromDateTime(summary.GetProperty("estimatedCompletionDate").GetDateTime()));
    }

    [Fact]
    public async Task CutJob_CanBeMovedToAnotherStationAndDay()
    {
        const string comp = "DndCut";
        var stationA = await _f.AddStationAsync("DndCut A", comp);
        var stationB = await _f.AddStationAsync("DndCut B", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);

        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(NewJob("DNDC-CUT", comp, 120, today.AddDays(5), status: "cut", stationId: stationA));
            await db.SaveChangesAsync();
        });

        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);
        Assert.Equal(stationA, (await ItemsAsync(admin, "DNDC-")).Single().GetProperty("stationId").GetInt32());

        var move = await admin.PostAsJsonAsync("/api/jobs/DNDC-CUT/schedule-manually", new { stationId = stationB, scheduleDate = today.AddDays(3) });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);

        var item = (await ItemsAsync(admin, "DNDC-")).Single(); // the old plan was replaced, not duplicated
        Assert.Equal(stationB, item.GetProperty("stationId").GetInt32());
        Assert.Equal(today.AddDays(3), DateOnly.FromDateTime(Dt(item, "plannedStart")));
    }

    [Fact]
    public async Task InProductionJob_CannotBeMoved()
    {
        const string comp = "DndRun";
        var station = await _f.AddStationAsync("DndRun Line", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(NewJob("DNDR-RUN", comp, 120, today.AddDays(5), status: "in-production", stationId: station));
            await db.SaveChangesAsync();
        });

        var admin = await _f.ClientAsync();
        var move = await admin.PostAsJsonAsync("/api/jobs/DNDR-RUN/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(2) });

        Assert.Equal(HttpStatusCode.BadRequest, move.StatusCode);
        Assert.Contains("already in production", await move.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Supervisor_CanMoveJobs()
    {
        const string comp = "DndSup";
        var station = await _f.AddStationAsync("DndSup Line", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(NewJob("DNDS-1", comp, 120, today.AddDays(5)));
            await db.SaveChangesAsync();
        });

        var admin = await _f.ClientAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new { username = "supervisor_dnd", password = "Super123!", role = "Supervisor" });
        Assert.True(create.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        var supervisor = await _f.ClientAsync("supervisor_dnd", "Super123!");

        var move = await supervisor.PostAsJsonAsync("/api/jobs/DNDS-1/schedule-manually", new { stationId = station, scheduleDate = today.AddDays(2) });
        Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await supervisor.DeleteAsync("/api/jobs/DNDS-1/schedule-manually")).StatusCode);
    }
}
