using ProductionScheduler.Domain;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

[CollectionDefinition(Name)]
public class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Integration";
}

// End-to-end harness for the §7 Step 8 scenarios: the real ASP.NET pipeline (auth, controllers,
// scheduler) against a dedicated SQL Server database, with the hosted background services
// disabled so each scenario drives every poll explicitly and deterministically.
public sealed class IntegrationFixture : IAsyncLifetime
{
    public const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=ProductionSchedulerIntegration;Trusted_Connection=True;TrustServerCertificate=True";

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
                services.AddDbContext<AppDbContext>(o => o.UseSqlServer(ConnectionString));
            });
        }
    }

    private TestFactory _factory = null!;
    private readonly Dictionary<string, HttpClient> _clients = new();

    public string TempRoot { get; } = Path.Combine(Path.GetTempPath(), "psint_" + Guid.NewGuid().ToString("N"));

    public IServiceProvider Services => _factory.Services;
    public IServiceScopeFactory ScopeFactory => Services.GetRequiredService<IServiceScopeFactory>();

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(TempRoot);

        // Migrate + wipe BEFORE the host starts, so its startup seeding sees a clean database.
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        await using (var db = new AppDbContext(options))
        {
            await db.Database.MigrateAsync();
            foreach (var table in new[]
                     {
                         "ScheduleItems", "JobMembers", "Precedences", "BatchcutEvents", "MaintenanceLogs", "PrdReadState",
                         "StationCapacityOverrides", "Jobs", "Operators", "ShiftBreaks", "Shifts", "SetupTimes",
                         "BatchcutWatchPaths", "Products", "JobTypes", "Stations", "Factories", "Settings", "Users",
                     })
            {
                await db.Database.ExecuteSqlRawAsync($"DELETE FROM [{table}]");
            }
        }

        _factory = new TestFactory();
        _ = _factory.Server; // build the host now (runs startup seeding)

        // Continuous 24h, 7-day calendar: wall-clock durations make every scenario's expected
        // timings independent of when the suite happens to run (nights, weekends).
        // The freeze window is off by default here so scheduling scenarios can move near-term work;
        // FreezeWindowTests turns it on for its own checks.
        await SetSettingsAsync(("ShiftStartHour", "0"), ("ShiftEndHour", "24"), ("WorkingDays", "0,1,2,3,4,5,6"), ("SchFileSettleSeconds", "0"),
            ("FreezeWindowDays", "0"));
    }

    public async Task DisposeAsync()
    {
        foreach (var client in _clients.Values) client.Dispose();
        await _factory.DisposeAsync();
        if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true);
    }

    // ---- database helpers ----

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = ScopeFactory.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = ScopeFactory.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task SetSettingsAsync(params (string Key, string Value)[] values) => WithDbAsync(async db =>
    {
        foreach (var (key, value) in values)
        {
            var existing = await db.Settings.FindAsync(key);
            if (existing is null) db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = SiteClock.Now });
            else { existing.Value = value; existing.UpdatedAt = SiteClock.Now; }
        }
        await db.SaveChangesAsync();
    });

    public string NewDir(string name)
    {
        var path = Path.Combine(TempRoot, name + "_" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(path);
        return path;
    }

    // prdPath = the station's DataOut (PRD files), mpsPath = its DataIn (MPS files), archivePath = its Archive.
    public Task<int> AddStationAsync(string name, string componentType, string? prdPath = null, string? mpsPath = null, string? archivePath = null, decimal? dailyEqATarget = null) =>
        WithDbAsync(async db =>
        {
            var station = new Station
            {
                Name = name, ComponentType = componentType, PrdPath = prdPath, MpsPath = mpsPath, ArchivePath = archivePath,
                DailyEqATarget = dailyEqATarget, IsActive = true,
            };
            db.Stations.Add(station);
            await db.SaveChangesAsync();
            return station.Id;
        });

    // ---- HTTP helpers ----

    public async Task<HttpClient> ClientAsync(string username = "admin", string password = "Admin123!")
    {
        if (_clients.TryGetValue(username, out var cached)) return cached;

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        _clients[username] = client;
        return client;
    }

    public HttpClient AnonymousClient() => _factory.CreateClient();

    // ---- background-service drivers (one explicit poll each) ----

    public Task PollSchImportAsync() => new SchImportService(ScopeFactory, NullLogger<SchImportService>.Instance).PollAsync(default);
    public Task PollMmeAsync() => new MmeFileWatcherService(ScopeFactory, NullLogger<MmeFileWatcherService>.Instance).PollAsync(default);
    public Task PollBatchcutAsync() => new BatchcutWatcherService(ScopeFactory, NullLogger<BatchcutWatcherService>.Instance).PollAsync(default);
    public Task PollPrdAsync() => new PrdReaderService(ScopeFactory, NullLogger<PrdReaderService>.Instance).PollAsync(default);
    public Task PollMpsAsync() => new MpsFileService(ScopeFactory, NullLogger<MpsFileService>.Instance).PollAsync(default);
    public Task RunNightlyAsync() => new NightlyMaintenanceService(ScopeFactory, NullLogger<NightlyMaintenanceService>.Instance).RunAllTasksAsync(default);
}
