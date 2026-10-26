using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Each station has three folders: PRD (DataOut), MPS (DataIn) and Archive.
[Collection(IntegrationCollection.Name)]
public class StationFoldersTests
{
    private readonly IntegrationFixture _f;

    public StationFoldersTests(IntegrationFixture fixture) => _f = fixture;

    [Fact]
    public async Task AllThreeFolders_RoundTrip_AndBlankMeansNotSet()
    {
        await _f.WithDbAsync(async db =>
        {
            if (!await db.Products.AnyAsync(p => p.Name == "FoldProduct")) db.Products.Add(new Product { Name = "FoldProduct", IsActive = true });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var created = await admin.PostAsJsonAsync("/api/stations", new
        {
            name = "Folder Line", componentType = "FoldProduct", prdPath = @"\\srv\Line\DataOut", mpsPath = @" \\srv\Line\DataIn ",
            archivePath = "   ", displayOrder = 1, dailyEqATarget = (decimal?)null,
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var station = await created.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(@"\\srv\Line\DataOut", station.GetProperty("prdPath").GetString());
        Assert.Equal(@"\\srv\Line\DataIn", station.GetProperty("mpsPath").GetString());
        Assert.Equal(JsonValueKind.Null, station.GetProperty("archivePath").ValueKind);
    }
}
