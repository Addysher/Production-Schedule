using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// v3.0 products: a product's name is the key stations, job types and jobs share, so it must exist
// before anything uses it, a rename has to follow it everywhere, and one in use can't be deleted.
[Collection(IntegrationCollection.Name)]
public class ProductsApiTests
{
    private readonly IntegrationFixture _f;

    public ProductsApiTests(IntegrationFixture fixture) => _f = fixture;

    private static async Task<int> CreateAsync(HttpClient admin, string name)
    {
        var response = await admin.PostAsJsonAsync("/api/products", new { name, poolPath = (string?)null, archivePath = (string?)null, isActive = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task Renaming_AProduct_RenamesItOnItsStationsAndJobs()
    {
        var admin = await _f.ClientAsync();
        var id = await CreateAsync(admin, "PrdOld");
        var station = await admin.PostAsJsonAsync("/api/stations", new { name = "Prd Line", componentType = "PrdOld", uncPath = (string?)null, displayOrder = 1, dailyEqATarget = (decimal?)null, archivePath = (string?)null });
        Assert.Equal(HttpStatusCode.Created, station.StatusCode);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(new Job { JobRef = "PRD-1", ComponentType = "PrdOld", DesignStatus = "Quote", Status = "scheduled", CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now });
            await db.SaveChangesAsync();
        });

        var rename = await admin.PutAsJsonAsync($"/api/products/{id}", new { name = "PrdNew", poolPath = "C:\\pool", archivePath = (string?)null, isActive = true });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        Assert.Equal("PrdNew", await _f.WithDbAsync(db => db.Stations.Where(s => s.Name == "Prd Line").Select(s => s.ComponentType).SingleAsync()));
        Assert.Equal("PrdNew", await _f.WithDbAsync(db => db.Jobs.Where(j => j.JobRef == "PRD-1").Select(j => j.ComponentType).SingleAsync()));
    }

    [Fact]
    public async Task AStation_CannotNameAProductThatDoesNotExist()
    {
        var admin = await _f.ClientAsync();
        var response = await admin.PostAsJsonAsync("/api/stations", new { name = "Typo Line", componentType = "RoofTruses", uncPath = (string?)null, displayOrder = 1, dailyEqATarget = (decimal?)null, archivePath = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AProductInUse_CannotBeDeleted_ButAnUnusedOneCan()
    {
        var admin = await _f.ClientAsync();
        var used = await CreateAsync(admin, "PrdUsed");
        await _f.AddStationAsync("PrdUsed Line", "PrdUsed");
        var unused = await CreateAsync(admin, "PrdUnused");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/products/{used}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{unused}")).StatusCode);
    }
}
