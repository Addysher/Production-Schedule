using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// v3.0 factories (grouping stations for the kiosk display) and the read-only Kiosk role.
[Collection(IntegrationCollection.Name)]
public class FactoryAndKioskTests
{
    private readonly IntegrationFixture _f;

    public FactoryAndKioskTests(IntegrationFixture fixture) => _f = fixture;

    private static async Task<int> IdOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

    [Fact]
    public async Task AStationCanBeAssignedToAFactory_AndDeletingTheFactoryLeavesTheStation()
    {
        var admin = await _f.ClientAsync();
        await _f.WithDbAsync(async db =>
        {
            if (!await db.Products.AnyAsync(p => p.Name == "FacProduct")) db.Products.Add(new Product { Name = "FacProduct", IsActive = true });
            await db.SaveChangesAsync();
        });
        var factoryId = await IdOf(await admin.PostAsJsonAsync("/api/factories", new { name = "Factory Test 1", isActive = true }));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/factories", new { name = "Factory Test 1", isActive = true })).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/stations", new
        {
            name = "Fac Line", componentType = "FacProduct", prdPath = (string?)null, mpsPath = (string?)null, displayOrder = 1, dailyEqATarget = 50, archivePath = (string?)null, factoryId,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var stationId = await IdOf(created);

        var factory = (await admin.GetFromJsonAsync<JsonElement>("/api/factories")).EnumerateArray().Single(f => f.GetProperty("id").GetInt32() == factoryId);
        Assert.Equal(stationId, Assert.Single(factory.GetProperty("stationIds").EnumerateArray()).GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/factories/{factoryId}")).StatusCode);
        Assert.Null(await _f.WithDbAsync(db => db.Stations.Where(s => s.Id == stationId).Select(s => s.FactoryId).SingleAsync()));
    }

    [Fact]
    public async Task AStationCantPointAtAMissingFactory()
    {
        var admin = await _f.ClientAsync();
        await _f.WithDbAsync(async db =>
        {
            if (!await db.Products.AnyAsync(p => p.Name == "FacProduct")) db.Products.Add(new Product { Name = "FacProduct", IsActive = true });
            await db.SaveChangesAsync();
        });

        var response = await admin.PostAsJsonAsync("/api/stations", new
        {
            name = "Fac Ghost", componentType = "FacProduct", prdPath = (string?)null, mpsPath = (string?)null, displayOrder = 1, dailyEqATarget = (decimal?)null, archivePath = (string?)null, factoryId = 999999,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AKioskAccount_CanReadTheDisplayData_ButChangeNothing()
    {
        var admin = await _f.ClientAsync();
        var factoryId = await IdOf(await admin.PostAsJsonAsync("/api/factories", new { name = "Kiosk Floor Factory", isActive = true }));
        var created = await admin.PostAsJsonAsync("/api/users", new { username = "kiosk_floor", password = "Pass1234!", role = "Kiosk", factoryId });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var kiosk = await _f.ClientAsync("kiosk_floor", "Pass1234!");

        foreach (var read in new[] { "/api/factories", "/api/stations", "/api/schedule/current", "/api/schedule/station-progress", "/api/jobs" })
        {
            Assert.Equal(HttpStatusCode.OK, (await kiosk.GetAsync(read)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PostAsync("/api/schedule/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PostAsJsonAsync("/api/factories", new { name = "Nope", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await kiosk.PostAsJsonAsync("/api/jobs/X/schedule-manually", new { stationId = 1, scheduleDate = "2030-01-01" })).StatusCode);
    }

    [Fact]
    public async Task EachFactoryHasOneKioskLogin_WhichOpensOntoItsDisplay()
    {
        var admin = await _f.ClientAsync();
        var factoryId = await IdOf(await admin.PostAsJsonAsync("/api/factories", new { name = "Kiosk Login Factory", isActive = true }));

        // A kiosk login must name its factory, and a factory gets only one.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = "kiosk_nofactory", password = "Pass1234!", role = "Kiosk" })).StatusCode);
        var kioskUser = await IdOf(await admin.PostAsJsonAsync("/api/users", new { username = "kiosk_one", password = "Pass1234!", role = "Kiosk", factoryId }));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/users", new { username = "kiosk_two", password = "Pass1234!", role = "Kiosk", factoryId })).StatusCode);

        var login = await (await _f.AnonymousClient().PostAsJsonAsync("/api/auth/login", new { username = "kiosk_one", password = "Pass1234!" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(factoryId, login.GetProperty("factoryId").GetInt32());

        // Other roles never carry a factory; deleting the factory leaves the login without one.
        var viewer = await (await admin.PostAsJsonAsync("/api/users", new { username = "kiosk_viewer", password = "Pass1234!", role = "Viewer", factoryId })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, viewer.GetProperty("factoryId").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/factories/{factoryId}")).StatusCode);
        Assert.Null(await _f.WithDbAsync(db => db.Users.Where(u => u.Id == kioskUser).Select(u => u.FactoryId).SingleAsync()));
    }
}
