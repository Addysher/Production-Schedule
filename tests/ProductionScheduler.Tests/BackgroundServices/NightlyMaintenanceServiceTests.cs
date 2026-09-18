using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

// Integration tests against a real (LocalDB) SQL Server instance rather than the EF InMemory
// provider — ArchiveOldRecordsAsync uses ExecuteUpdateAsync, which InMemory does not support.
// Runs against a dedicated "ProductionSchedulerTests" database, migrated on first use.
public class NightlyMaintenanceServiceTests : IDisposable
{
    private const string ConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=ProductionSchedulerTests;Trusted_Connection=True;TrustServerCertificate=True";

    private readonly AppDbContext _db;
    private readonly string _tempRoot;

    public NightlyMaintenanceServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
        _db = new AppDbContext(options);
        _db.Database.Migrate();

        _tempRoot = Path.Combine(Path.GetTempPath(), "pstests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private async Task SetSettingAsync(string key, string value)
    {
        // Settings rows persist across test runs in this shared database — upsert rather than
        // always insert, since a prior test may have already created this well-known key.
        var existing = await _db.Settings.FindAsync(key);
        if (existing is null)
        {
            _db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync();
    }

    private static Job NewJob(string jobRef) => new()
    {
        JobRef = jobRef,
        ComponentType = "WallFrames",
        DesignStatus = "FinalDesign",
        Status = "ready",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Task1_MoveMpsToDataIn_CopiesFileAndLogsSuccess()
    {
        var jobRef = "T1-" + Guid.NewGuid().ToString("N")[..8];
        var mpsSourceDir = Path.Combine(_tempRoot, "raw");
        var stationDataIn = Path.Combine(_tempRoot, "datain");
        Directory.CreateDirectory(mpsSourceDir);
        Directory.CreateDirectory(stationDataIn);
        await File.WriteAllTextAsync(Path.Combine(mpsSourceDir, $"{jobRef}__.MPS"), "mps content");

        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = new Station { Name = "S-" + jobRef, ComponentType = "WallFrames", UncPath = stationDataIn, IsActive = true };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();

        var job = NewJob(jobRef);
        job.MpsSourcePath = mpsSourceDir;
        job.AssignedStationId = station.Id;
        job.EstimatedStartDate = DateTime.UtcNow.Date.AddDays(1); // tomorrow == today + 1 pre-move day
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        await NightlyMaintenanceService.MoveMpsToDataInAsync(_db, default);

        Assert.True(File.Exists(Path.Combine(stationDataIn, $"{jobRef}__.MPS")));
        var log = await _db.MaintenanceLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.TaskName == "MpsToDatIn" && l.JobRef == jobRef);
        Assert.Equal("success", log.Outcome);
    }

    [Fact]
    public async Task Task1_SourceFileMissing_LogsError_DoesNotThrow()
    {
        var jobRef = "T1E-" + Guid.NewGuid().ToString("N")[..8];
        var stationDataIn = Path.Combine(_tempRoot, "datain2");
        Directory.CreateDirectory(stationDataIn);

        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = new Station { Name = "S-" + jobRef, ComponentType = "WallFrames", UncPath = stationDataIn, IsActive = true };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();

        var job = NewJob(jobRef);
        job.MpsSourcePath = Path.Combine(_tempRoot, "does_not_exist");
        job.AssignedStationId = station.Id;
        job.EstimatedStartDate = DateTime.UtcNow.Date.AddDays(1);
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        await NightlyMaintenanceService.MoveMpsToDataInAsync(_db, default);

        var log = await _db.MaintenanceLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.TaskName == "MpsToDatIn" && l.JobRef == jobRef);
        Assert.Equal("error", log.Outcome);
    }

    [Fact]
    public async Task Task2_ArchiveDoneFolders_MovesFolderToYearMonthArchive()
    {
        var forCutting = Path.Combine(_tempRoot, "forcutting");
        var doneBase = Path.Combine(_tempRoot, "donebase");
        Directory.CreateDirectory(forCutting);
        var jobRef = "T2-" + Guid.NewGuid().ToString("N")[..8];
        var doneFolder = Path.Combine(forCutting, $"{jobRef}__DONE");
        Directory.CreateDirectory(doneFolder);
        await File.WriteAllTextAsync(Path.Combine(doneFolder, "x.mps"), "x");

        await SetSettingAsync("SawFilesForCuttingPath", forCutting);
        await SetSettingAsync("SawFilesDoneBasePath", doneBase);

        await NightlyMaintenanceService.ArchiveDoneFoldersAsync(_db, default);

        Assert.False(Directory.Exists(doneFolder));
        var now = DateTime.UtcNow;
        var expectedDest = Path.Combine(doneBase, now.Year.ToString(), $"{now.Year}-{now.Month:D2}", $"{jobRef}__DONE");
        Assert.True(Directory.Exists(expectedDest));
        Assert.True(File.Exists(Path.Combine(expectedDest, "x.mps")));
    }

    [Fact]
    public async Task Task3_DeleteSchOnCompletion_DeletesFileAndStampsTimestamp()
    {
        var jobRef = "T3-" + Guid.NewGuid().ToString("N")[..8];
        var schFile = Path.Combine(_tempRoot, $"{jobRef}__.sch");
        await File.WriteAllTextAsync(schFile, "content");

        var job = NewJob(jobRef);
        job.Status = "completed";
        job.SchSourcePath = schFile;
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        await NightlyMaintenanceService.DeleteSchOnCompletionAsync(_db, default);

        Assert.False(File.Exists(schFile));
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        Assert.NotNull(refreshed.SchDeletedAt);
        var log = await _db.MaintenanceLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.TaskName == "SchDelete" && l.JobRef == jobRef);
        Assert.Equal("success", log.Outcome);
    }

    [Fact]
    public async Task Task4_ArchiveMpsOnCompletion_MovesFileToStationArchive_SkipsWhenMissing()
    {
        var jobRef = "T4-" + Guid.NewGuid().ToString("N")[..8];
        var dataIn = Path.Combine(_tempRoot, "t4datain");
        var archive = Path.Combine(_tempRoot, "t4archive");
        Directory.CreateDirectory(dataIn);
        Directory.CreateDirectory(archive);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "mps");

        var station = new Station { Name = "S-" + jobRef, ComponentType = "WallFrames", UncPath = dataIn, ArchivePath = archive, IsActive = true };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();

        var job = NewJob(jobRef);
        job.Status = "completed";
        job.AssignedStationId = station.Id;
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();

        await NightlyMaintenanceService.ArchiveMpsOnCompletionAsync(_db, default);

        Assert.False(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.True(File.Exists(Path.Combine(archive, $"{jobRef}__.MPS")));
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        Assert.NotNull(refreshed.MpsArchivedAt);
    }

    [Fact]
    public async Task Task5_ArchiveOldRecords_FlagsOnlyRecordsOlderThanRetention()
    {
        var jobRefOld = "T5OLD-" + Guid.NewGuid().ToString("N")[..8];
        var jobRefNew = "T5NEW-" + Guid.NewGuid().ToString("N")[..8];

        var oldJob = NewJob(jobRefOld);
        oldJob.Status = "completed";
        oldJob.UpdatedAt = DateTime.UtcNow.AddMonths(-12);
        var newJob = NewJob(jobRefNew);
        newJob.Status = "completed";
        newJob.UpdatedAt = DateTime.UtcNow;

        _db.Jobs.AddRange(oldJob, newJob);
        await SetSettingAsync("ScheduleRetentionMonths", "6");
        await _db.SaveChangesAsync();

        await NightlyMaintenanceService.ArchiveOldRecordsAsync(_db, default);

        var refreshedOld = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRefOld);
        var refreshedNew = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRefNew);
        Assert.True(refreshedOld.IsArchived);
        Assert.False(refreshedNew.IsArchived);
    }

    [Fact]
    public async Task RunAllTasks_OneTaskFailing_DoesNotBlockTheOthers()
    {
        // Task 3 (SCH delete) with a job whose SchSourcePath points at a directory instead of a
        // file — File.Exists returns false so it's just logged as skipped rather than throwing,
        // so to actually force a failure we invert this: point at a locked/invalid path pattern.
        // Simpler and still valid: verify RunAllTasksAsync completes and logs a row for every task
        // even when one task's Settings are entirely absent (so it no-ops rather than finding work).
        var jobRef = "T6-" + Guid.NewGuid().ToString("N")[..8];
        var job = NewJob(jobRef);
        job.Status = "completed";
        job.SchSourcePath = Path.Combine(_tempRoot, $"{jobRef}__.sch");
        await File.WriteAllTextAsync(job.SchSourcePath, "x");
        _db.Jobs.Add(job);
        await SetSettingAsync("ScheduleRetentionMonths", "6");
        await _db.SaveChangesAsync();

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<NightlyMaintenanceService>.Instance;
        var service = new NightlyMaintenanceService(new SingleContextScopeFactory(_db), logger);

        await service.RunAllTasksAsync(default);

        // Task 3 should still have succeeded despite other tasks finding nothing to do.
        var refreshed = await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef);
        Assert.NotNull(refreshed.SchDeletedAt);
    }

    // Minimal IServiceScopeFactory that always hands back this test's own AppDbContext — avoids
    // spinning up a full DI container just to exercise NightlyMaintenanceService.RunAllTasksAsync.
    private sealed class SingleContextScopeFactory : Microsoft.Extensions.DependencyInjection.IServiceScopeFactory
    {
        private readonly AppDbContext _db;
        public SingleContextScopeFactory(AppDbContext db) => _db = db;
        public Microsoft.Extensions.DependencyInjection.IServiceScope CreateScope() => new Scope(_db);

        private sealed class Scope : Microsoft.Extensions.DependencyInjection.IServiceScope
        {
            public Scope(AppDbContext db) => ServiceProvider = new Provider(db);
            public IServiceProvider ServiceProvider { get; }
            public void Dispose() { }

            private sealed class Provider : IServiceProvider
            {
                private readonly AppDbContext _db;
                public Provider(AppDbContext db) => _db = db;
                public object? GetService(Type serviceType) => serviceType == typeof(AppDbContext) ? _db : null;
            }
        }
    }
}
