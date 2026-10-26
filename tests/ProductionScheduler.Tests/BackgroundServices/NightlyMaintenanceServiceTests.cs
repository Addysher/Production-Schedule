using ProductionScheduler.Domain;
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
            _db.Settings.Add(new Setting { Key = key, Value = value, UpdatedAt = SiteClock.Now });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = SiteClock.Now;
        }
        await _db.SaveChangesAsync();
    }

    private static Job NewJob(string jobRef) => new()
    {
        JobRef = jobRef,
        ComponentType = "WallFrames",
        DesignStatus = "FinalDesign",
        Status = "ready",
        CreatedAt = SiteClock.Now,
        UpdatedAt = SiteClock.Now
    };

    // ---- MPS lifecycle (v3.0): pool -> station DataIn -> station archive ----

    private async Task<Station> AddStationAsync(string name, string dataIn, string? archive = null)
    {
        var station = new Station { Name = name, ComponentType = "WallFrames", MpsPath = dataIn, ArchivePath = archive, IsActive = true };
        _db.Stations.Add(station);
        await _db.SaveChangesAsync();
        return station;
    }

    private string NewDir(string name)
    {
        var dir = Path.Combine(_tempRoot, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<Job> AddJobAsync(string jobRef, string pool, int? stationId, DateTime? start, string status = "approved", int? mpsStationId = null)
    {
        var job = NewJob(jobRef);
        job.Status = status;
        job.MpsSourcePath = pool;
        job.AssignedStationId = stationId;
        job.EstimatedStartDate = start;
        job.MpsStationId = mpsStationId;
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync();
        return job;
    }

    private Task<MaintenanceLog> LastLogAsync(string task, string jobRef) =>
        _db.MaintenanceLogs.OrderByDescending(l => l.Id).FirstAsync(l => l.TaskName == task && l.JobRef == jobRef);

    [Fact]
    public async Task Task1_SendDue_MovesMpsFromPoolIntoStationDataIn()
    {
        var jobRef = "T1-" + Guid.NewGuid().ToString("N")[..8];
        var pool = NewDir("pool");
        var dataIn = NewDir("datain");
        await File.WriteAllTextAsync(Path.Combine(pool, $"{jobRef}__.MPS"), "mps content");
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = await AddStationAsync("S-" + jobRef, dataIn);
        await AddJobAsync(jobRef, pool, station.Id, SiteClock.Now.Date.AddDays(1)); // starts tomorrow

        await MpsFiles.SendDueAsync(_db, default);

        Assert.True(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(pool, $"{jobRef}__.MPS")), "moved, not copied");
        Assert.Equal(station.Id, (await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef)).MpsStationId);
        Assert.Equal("success", (await LastLogAsync("MpsToDataIn", jobRef)).Outcome);
    }

    [Fact]
    public async Task Task1_MpsNotInPool_LogsError_DoesNotThrow()
    {
        var jobRef = "T1E-" + Guid.NewGuid().ToString("N")[..8];
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = await AddStationAsync("S-" + jobRef, NewDir("datain2"));
        await AddJobAsync(jobRef, Path.Combine(_tempRoot, "does_not_exist"), station.Id, SiteClock.Now.Date.AddDays(1));

        await MpsFiles.SendDueAsync(_db, default);

        Assert.Equal("error", (await LastLogAsync("MpsToDataIn", jobRef)).Outcome);
    }

    [Fact]
    public async Task Reconcile_JobRescheduledToAnotherStation_MpsFollowsIt()
    {
        var jobRef = "RF-" + Guid.NewGuid().ToString("N")[..8];
        var oldIn = NewDir("rf-old");
        var newIn = NewDir("rf-new");
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var oldStation = await AddStationAsync("Old-" + jobRef, oldIn);
        var newStation = await AddStationAsync("New-" + jobRef, newIn);
        await File.WriteAllTextAsync(Path.Combine(oldIn, $"{jobRef}__.MPS"), "mps");
        await AddJobAsync(jobRef, NewDir("rf-pool"), newStation.Id, SiteClock.Now.Date.AddDays(1), mpsStationId: oldStation.Id);

        await MpsFiles.ReconcileAsync(_db, default);

        Assert.True(File.Exists(Path.Combine(newIn, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(oldIn, $"{jobRef}__.MPS")));
        Assert.Equal(newStation.Id, (await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef)).MpsStationId);
    }

    [Fact]
    public async Task Reconcile_JobRescheduledOutOfRange_MpsGoesBackToThePool()
    {
        var jobRef = "RB-" + Guid.NewGuid().ToString("N")[..8];
        var dataIn = NewDir("rb-datain");
        var pool = NewDir("rb-pool");
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = await AddStationAsync("S-" + jobRef, dataIn);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "mps");
        await AddJobAsync(jobRef, pool, station.Id, SiteClock.Now.Date.AddDays(10), mpsStationId: station.Id); // moved to next week

        await MpsFiles.ReconcileAsync(_db, default);

        Assert.True(File.Exists(Path.Combine(pool, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.Null((await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef)).MpsStationId);
    }

    [Fact]
    public async Task Reconcile_CompletedJob_MpsArchivedFromTheStation_OnlyAfterShiftEnd()
    {
        var jobRef = "CA-" + Guid.NewGuid().ToString("N")[..8];
        var dataIn = NewDir("ca-datain");
        var archive = NewDir("ca-archive");
        var station = await AddStationAsync("S-" + jobRef, dataIn, archive);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "mps");
        await AddJobAsync(jobRef, NewDir("ca-pool"), station.Id, SiteClock.Now.Date, status: "completed", mpsStationId: station.Id);

        await SetSettingAsync("ShiftEndHour", "24"); // shift still running
        await MpsFiles.ReconcileAsync(_db, default);
        Assert.True(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")), "left in DataIn during the shift");

        await SetSettingAsync("ShiftEndHour", "0"); // shift over
        await MpsFiles.ReconcileAsync(_db, default);
        await SetSettingAsync("ShiftEndHour", "18");

        Assert.True(File.Exists(Path.Combine(archive, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.NotNull((await _db.Jobs.AsNoTracking().FirstAsync(j => j.JobRef == jobRef)).MpsArchivedAt);
    }

    [Fact]
    public async Task Reconcile_RevisedMpsInThePool_ReplacesTheStationCopy_AndKeepsTheOldOneAside()
    {
        var jobRef = "RV-" + Guid.NewGuid().ToString("N")[..8];
        var pool = NewDir("rv-pool");
        var dataIn = NewDir("rv-datain");
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        var station = await AddStationAsync("S-" + jobRef, dataIn);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "old design");
        await File.WriteAllTextAsync(Path.Combine(pool, $"{jobRef}__.MPS"), "revised design");
        await AddJobAsync(jobRef, pool, station.Id, SiteClock.Now.Date.AddDays(1), mpsStationId: station.Id);

        await MpsFiles.ReconcileAsync(_db, default);

        Assert.Equal("revised design", await File.ReadAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(pool, $"{jobRef}__.MPS")));
        Assert.Single(Directory.GetFiles(Path.Combine(dataIn, "_superseded"), $"{jobRef}*"));
    }

    [Fact]
    public async Task Reconcile_JobFinishedOnAnEarlierDay_IsArchivedEvenMidShift()
    {
        var jobRef = "ED-" + Guid.NewGuid().ToString("N")[..8];
        var dataIn = NewDir("ed-datain");
        var archive = NewDir("ed-archive");
        var station = await AddStationAsync("S-" + jobRef, dataIn, archive);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "mps");
        var job = await AddJobAsync(jobRef, NewDir("ed-pool"), station.Id, SiteClock.Now.Date.AddDays(-2), status: "completed", mpsStationId: station.Id);
        job.UpdatedAt = SiteClock.Now.AddDays(-1); // it finished yesterday
        await _db.SaveChangesAsync();

        await SetSettingAsync("ShiftEndHour", "24"); // a shift that runs to midnight is never "over" on the same day
        await MpsFiles.ReconcileAsync(_db, default);
        await SetSettingAsync("ShiftEndHour", "18");

        Assert.True(File.Exists(Path.Combine(archive, $"{jobRef}__.MPS")));
    }

    [Fact]
    public async Task DeletedJob_MpsArchivedFromTheStation()
    {
        var jobRef = "DL-" + Guid.NewGuid().ToString("N")[..8];
        var dataIn = NewDir("dl-datain");
        var archive = NewDir("dl-archive");
        var station = await AddStationAsync("S-" + jobRef, dataIn, archive);
        await File.WriteAllTextAsync(Path.Combine(dataIn, $"{jobRef}__.MPS"), "mps");
        var job = await AddJobAsync(jobRef, NewDir("dl-pool"), station.Id, SiteClock.Now.Date.AddDays(1), mpsStationId: station.Id);

        await MpsFiles.ArchiveForDeletedJobAsync(_db, job);
        await _db.SaveChangesAsync();

        Assert.True(File.Exists(Path.Combine(archive, $"{jobRef}__.MPS")));
        Assert.False(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
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
        var now = SiteClock.Now;
        var expectedDest = Path.Combine(doneBase, now.Year.ToString(), $"{now.Year}-{now.Month:D2}", $"{jobRef}__DONE");
        Assert.True(Directory.Exists(expectedDest));
        Assert.True(File.Exists(Path.Combine(expectedDest, "x.mps")));
    }

    [Fact]
    public async Task Task5_ArchiveOldRecords_FlagsOnlyRecordsOlderThanRetention()
    {
        var jobRefOld = "T5OLD-" + Guid.NewGuid().ToString("N")[..8];
        var jobRefNew = "T5NEW-" + Guid.NewGuid().ToString("N")[..8];

        var oldJob = NewJob(jobRefOld);
        oldJob.Status = "completed";
        oldJob.UpdatedAt = SiteClock.Now.AddMonths(-12);
        var newJob = NewJob(jobRefNew);
        newJob.Status = "completed";
        newJob.UpdatedAt = SiteClock.Now;

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
    public async Task RunAllTasks_RunsTheMpsSend_EvenWhenOtherTasksHaveNothingToDo()
    {
        var jobRef = "T6-" + Guid.NewGuid().ToString("N")[..8];
        var pool = NewDir("t6-pool");
        var dataIn = NewDir("t6-datain");
        await File.WriteAllTextAsync(Path.Combine(pool, $"{jobRef}__.MPS"), "mps");
        await SetSettingAsync("MpsPreMoveDaysBeforeProduction", "1");
        await SetSettingAsync("ScheduleRetentionMonths", "6");
        var station = await AddStationAsync("S-" + jobRef, dataIn);
        await AddJobAsync(jobRef, pool, station.Id, SiteClock.Now.Date.AddDays(1));

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<NightlyMaintenanceService>.Instance;
        var service = new NightlyMaintenanceService(new SingleContextScopeFactory(_db), logger);

        await service.RunAllTasksAsync(default);

        Assert.True(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
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
