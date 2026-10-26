using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario E: jobs in states that trigger each nightly task; run the service; verify
// the files land where they should and MaintenanceLogs records every outcome — and that one
// task failing does not stop the others.
// v3.0: Task 1 moves the MPS out of the product pool (it no longer copies); SCH files are archived
// at import and kept (no deletion on completion); MPS archiving happens at end of shift (MpsFileTests).
[Collection(IntegrationCollection.Name)]
public class ScenarioE_NightlyTasksTests
{
    private readonly IntegrationFixture _f;

    public ScenarioE_NightlyTasksTests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string status) => new()
    {
        JobRef = jobRef, ComponentType = "ScnE", DesignStatus = "FinalDesign", Status = status,
        CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private sealed record World(string Pool, string DataIn, string ForCutting, string DoneBase, int StationId);

    private async Task<World> BuildWorldAsync(string tag, string? doneBaseOverride = null)
    {
        var pool = _f.NewDir($"e{tag}pool");
        var dataIn = _f.NewDir($"e{tag}datain");
        var forCutting = _f.NewDir($"e{tag}forcutting");
        var doneBase = doneBaseOverride ?? _f.NewDir($"e{tag}donebase");
        await _f.SetSettingsAsync(
            ("SawFilesForCuttingPath", forCutting), ("SawFilesDoneBasePath", doneBase),
            ("MpsPreMoveDaysBeforeProduction", "1"), ("ScheduleRetentionMonths", "6"));
        var stationId = await _f.AddStationAsync($"ScnE {tag}", "ScnE", mpsPath: dataIn, archivePath: _f.NewDir($"e{tag}archive"));
        return new World(pool, dataIn, forCutting, doneBase, stationId);
    }

    // Task 1: a job starting tomorrow with its MPS in the pool, and one whose MPS never arrived.
    // Task 2: an operator-renamed DONE folder. Plus a completed job whose archived SCH must be kept.
    private async Task SeedAsync(World w, string tag)
    {
        await File.WriteAllTextAsync(Path.Combine(w.Pool, $"E{tag}T1__.MPS"), "mps");
        Directory.CreateDirectory(Path.Combine(w.ForCutting, $"E{tag}T2__DONE"));
        var archivedSch = Path.Combine(w.Pool, $"E{tag}DONE__.sch");
        await File.WriteAllTextAsync(archivedSch, "sch");

        await _f.WithDbAsync(async db =>
        {
            var t1 = NewJob($"E{tag}T1", "approved");
            t1.MpsSourcePath = w.Pool; t1.AssignedStationId = w.StationId; t1.EstimatedStartDate = SiteClock.Now.Date.AddDays(1);
            var missing = NewJob($"E{tag}NOMPS", "approved");
            missing.MpsSourcePath = w.Pool; missing.AssignedStationId = w.StationId; missing.EstimatedStartDate = SiteClock.Now.Date.AddDays(1);
            var done = NewJob($"E{tag}DONE", "completed");
            done.SchSourcePath = archivedSch;
            db.Jobs.AddRange(t1, missing, done);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task EachTask_DoesItsJob_AndEveryOutcomeIsLogged()
    {
        var w = await BuildWorldAsync("1");
        await SeedAsync(w, "1");

        await _f.RunNightlyAsync();

        Assert.True(File.Exists(Path.Combine(w.DataIn, "E1T1__.MPS")), "Task 1: MPS moved into DataIn");
        Assert.False(File.Exists(Path.Combine(w.Pool, "E1T1__.MPS")), "Task 1: moved, not copied");
        var now = SiteClock.Now;
        Assert.True(Directory.Exists(Path.Combine(w.DoneBase, now.Year.ToString(), $"{now.Year}-{now.Month:D2}", "E1T2__DONE")), "Task 2: DONE folder archived");
        Assert.True(File.Exists(Path.Combine(w.Pool, "E1DONE__.sch")), "archived SCH files are kept");

        var jobs = await _f.WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.JobRef.StartsWith("E1")).ToDictionaryAsync(j => j.JobRef));
        Assert.Equal(w.StationId, jobs["E1T1"].MpsStationId);
        Assert.Null(jobs["E1NOMPS"].MpsStationId);

        var logs = await _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().ToListAsync());
        Assert.Contains(logs, l => l.TaskName == "MpsToDataIn" && l.JobRef == "E1T1" && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "MpsToDataIn" && l.JobRef == "E1NOMPS" && l.Outcome == "error");
        Assert.Contains(logs, l => l.TaskName == "SawFilesCleanup" && l.SourcePath!.Contains("E1T2__DONE") && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "RetentionArchive" && l.Outcome == "success");
    }

    [Fact]
    public async Task ATaskThatThrows_IsLoggedAsAnError_AndDoesNotBlockTheOthers()
    {
        // Task 2's archive root is an impossible path, so it throws while creating the destination.
        var w = await BuildWorldAsync("2", doneBaseOverride: Path.Combine(_f.TempRoot, "bad|archive:root"));
        await SeedAsync(w, "2");

        await _f.RunNightlyAsync();

        var logs = await _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().ToListAsync());
        Assert.Contains(logs, l => l.TaskName.StartsWith("Task 2") && l.Outcome == "error");

        // Task 1 and retention still ran to completion.
        Assert.True(File.Exists(Path.Combine(w.DataIn, "E2T1__.MPS")));
        Assert.Contains(logs, l => l.TaskName == "RetentionArchive" && l.Outcome == "success");
        Assert.True(Directory.Exists(Path.Combine(w.ForCutting, "E2T2__DONE")), "the DONE folder must be left in place, not lost");
    }
}
