using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Account changes take effect immediately, the system can't be left without an Admin, and watch
// paths with history are switched off rather than failing to delete.
[Collection(IntegrationCollection.Name)]
public class AccountSafetyTests
{
    private readonly IntegrationFixture _f;

    public AccountSafetyTests(IntegrationFixture fixture) => _f = fixture;

    private async Task<(HttpClient Client, int Id)> NewUserAsync(string username, string role)
    {
        var admin = await _f.ClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users", new { username, password = "Pass1234!", role });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        return (await _f.ClientAsync(username, "Pass1234!"), id);
    }

    [Fact]
    public async Task ADeactivatedUser_IsLockedOutStraightAway_NotWhenTheirTokenExpires()
    {
        var (client, id) = await NewUserAsync("acct_deact", "Viewer");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs")).StatusCode);

        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobs")).StatusCode);
    }

    [Fact]
    public async Task ARoleChange_TakesEffectOnTheNextRequest()
    {
        var (client, id) = await NewUserAsync("acct_demote", "Admin");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/users")).StatusCode);

        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/users/{id}", new { role = "Viewer", isActive = true })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task TheLastActiveAdmin_CannotBeDemotedOrDeactivated()
    {
        var admin = await _f.ClientAsync();
        // Make sure "admin" is the only active Admin for this check.
        await _f.WithDbAsync(async db =>
        {
            foreach (var other in await db.Users.Where(u => u.Role == "Admin" && u.Username != "admin").ToListAsync()) other.IsActive = false;
            await db.SaveChangesAsync();
        });
        var adminId = await _f.WithDbAsync(db => db.Users.Where(u => u.Username == "admin").Select(u => u.Id).SingleAsync());

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/users/{adminId}", new { role = "Viewer", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/users/{adminId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/users")).StatusCode); // still an Admin
    }

    [Fact]
    public async Task DeletingAWatchPathWithHistory_SwitchesItOffInstead()
    {
        var admin = await _f.ClientAsync();
        var created = await (await admin.PostAsJsonAsync("/api/admin/batchcut-watch-paths", new { name = "Hist path", uncPath = "C:\\hist", isActive = true }))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();
        await _f.WithDbAsync(async db =>
        {
            db.BatchcutEvents.Add(new BatchcutEvent { BatchcutPathId = id, FolderName = "X__DONE", DetectedAt = ProductionScheduler.Domain.SiteClock.Now, Status = "error" });
            await db.SaveChangesAsync();
        });

        var delete = await admin.DeleteAsync($"/api/admin/batchcut-watch-paths/{id}");

        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        Assert.False(await _f.WithDbAsync(db => db.BatchcutWatchPaths.Where(p => p.Id == id).Select(p => p.IsActive).SingleAsync()));
    }
}
