using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario D: a Viewer token gets 403 on every mutating API call; anonymous callers
// get 401 everywhere except login. Routes are discovered from the live endpoint table, so a
// controller added later without [Authorize(Roles = "Admin")] fails this test instead of slipping by.
[Collection(IntegrationCollection.Name)]
public class ScenarioD_RoleEnforcementTests
{
    private readonly IntegrationFixture _f;

    public ScenarioD_RoleEnforcementTests(IntegrationFixture fixture) => _f = fixture;

    // Mutating routes that are deliberately open to any authenticated user.
    private static readonly HashSet<string> OpenToAnyRole = new(StringComparer.OrdinalIgnoreCase)
    {
        "api/auth/logout", "api/auth/change-password", "api/quotes/estimate",
    };

    private List<(string Method, string Path)> DiscoverApiRoutes() =>
        _f.Services.GetServices<EndpointDataSource>()
            .SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText is { } raw && raw.TrimStart('/').StartsWith("api/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(e =>
            {
                var raw = e.RoutePattern.RawText!.TrimStart('/');
                var path = "/" + Regex.Replace(raw, @"\{[^}]+\}", "1");
                var methods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>();
                return methods.Select(m => (m, path));
            })
            .Distinct()
            .ToList();

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path) { Content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null };

    private async Task<HttpClient> ViewerAsync()
    {
        var admin = await _f.ClientAsync();
        var create = await admin.PostAsJsonAsync("/api/users", new { username = "viewer_d", password = "Viewer123!", role = "Viewer" });
        Assert.True(create.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict);
        return await _f.ClientAsync("viewer_d", "Viewer123!");
    }

    [Fact]
    public async Task Viewer_GetsForbiddenOnEveryMutatingEndpoint()
    {
        var viewer = await ViewerAsync();
        var mutating = DiscoverApiRoutes()
            .Where(r => r.Method is "POST" or "PUT" or "DELETE")
            .Where(r => !OpenToAnyRole.Contains(r.Path.TrimStart('/')) && !r.Path.EndsWith("/auth/login"))
            .ToList();

        Assert.True(mutating.Count >= 30, $"expected to discover the full mutating surface, found {mutating.Count}");

        var failures = new List<string>();
        foreach (var (method, path) in mutating)
        {
            var response = await viewer.SendAsync(Request(method, path));
            if (response.StatusCode != HttpStatusCode.Forbidden) failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Anonymous_GetsUnauthorizedEverywhereExceptLogin()
    {
        var anon = _f.AnonymousClient();
        var failures = new List<string>();

        foreach (var (method, path) in DiscoverApiRoutes().Where(r => r.Path != "/api/auth/login"))
        {
            var response = await anon.SendAsync(Request(method, path));
            if (response.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public async Task Viewer_CanReadSharedViews_ButNotAdminOnlyConfiguration()
    {
        var viewer = await ViewerAsync();

        foreach (var path in new[]
                 {
                     "/api/jobs", "/api/jobs/status-refresh", "/api/schedule/current", "/api/schedule/export",
                     "/api/stations", "/api/jobtypes", "/api/maintenance-logs",
                 })
        {
            Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync(path)).StatusCode);
        }

        foreach (var path in new[]
                 {
                     "/api/users", "/api/settings", "/api/operators", "/api/shifts", "/api/stations/1/capacity-overrides",
                     "/api/admin/sch-import-paths", "/api/admin/batchcut-watch-paths", "/api/admin/batchcut-events",
                 })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync(path)).StatusCode);
        }

        // The Quote Estimator is open to any signed-in user (it is a POST, but read-only in effect).
        var estimate = await viewer.PostAsJsonAsync("/api/quotes/estimate", new { @ref = "Q", lines = Array.Empty<object>() });
        Assert.NotEqual(HttpStatusCode.Forbidden, estimate.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, estimate.StatusCode);
    }

    [Fact]
    public async Task DeactivatedUser_CanNoLongerLogIn()
    {
        var admin = await _f.ClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users", new { username = "viewer_gone", password = "Viewer123!", role = "Viewer" });
        var id = (await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/users/{id}")).StatusCode);

        var login = await _f.AnonymousClient().PostAsJsonAsync("/api/auth/login", new { username = "viewer_gone", password = "Viewer123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }
}
