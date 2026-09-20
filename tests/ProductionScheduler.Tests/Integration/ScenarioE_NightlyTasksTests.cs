using ProductionScheduler.Domain;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario E: jobs in states that trigger each nightly task; run the service; verify
// the files land where they should and MaintenanceLogs records every outcome — and that one
// task failing does not stop the others.
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

    private sealed record World(string RawDir, string DataIn, string Archive, string ForCutting, string DoneBase, int StationId);

    private async Task<World> BuildWorldAsync(string tag, string? doneBaseOverride = null)
    {
        var raw = _f.NewDir($"e{tag}raw");
        var dataIn = _f.NewDir($"e{tag}datain");
        var archive = _f.NewDir($"e{tag}archive");
        var forCutting = _f.NewDir($"e{tag}forcutting");
        var doneBase = doneBaseOverride ?? _f.NewDir($"e{tag}donebase");
        await _f.SetSettingsAsync(
            ("SawFilesForCuttingPath", forCutting), ("SawFilesDoneBasePath", doneBase),
            ("MpsPreMoveDaysBeforeProduction", "1"), ("ScheduleRetentionMonths", "6"));
        var stationId = await _f.AddStationAsync($"ScnE {tag}", "ScnE", dataIn, archive);
        return new World(raw, dataIn, archive, forCutting, doneBase, stationId);
    }

    // Seeds one job for each of Tasks 1, 3 and 4, plus a DONE folder for Task 2.
    private async Task SeedAllFourTriggersAsync(World w, string tag)
    {
        // Task 1: starts tomorrow, MPS waiting in the source folder.
        await File.WriteAllTextAsync(Path.Combine(w.RawDir, $"E{tag}T1__.MPS"), "mps");
        // Task 3: completed, SCH file still on disk.
        var sch = Path.Combine(w.RawDir, $"E{tag}T3__.sch");
        await File.WriteAllTextAsync(sch, "sch");
        // Task 4: completed, MPS still sitting in the station's DataIn.
        await File.WriteAllTextAsync(Path.Combine(w.DataIn, $"E{tag}T4__.MPS"), "mps");
        // Task 2: an operator-renamed DONE folder awaiting archive.
        Directory.CreateDirectory(Path.Combine(w.ForCutting, $"E{tag}T2__DONE"));

        await _f.WithDbAsync(async db =>
        {
            var t1 = NewJob($"E{tag}T1", "approved");
            t1.MpsSourcePath = w.RawDir; t1.AssignedStationId = w.StationId; t1.EstimatedStartDate = SiteClock.Now.Date.AddDays(1);
            var t3 = NewJob($"E{tag}T3", "completed");
            t3.SchSourcePath = sch;
            var t4 = NewJob($"E{tag}T4", "completed");
            t4.AssignedStationId = w.StationId;
            db.Jobs.AddRange(t1, t3, t4);
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task EachTask_DoesItsJob_AndEveryOutcomeIsLogged()
    {
        var w = await BuildWorldAsync("1");
        await SeedAllFourTriggersAsync(w, "1");

        await _f.RunNightlyAsync();

        Assert.True(File.Exists(Path.Combine(w.DataIn, "E1T1__.MPS")), "Task 1: MPS copied into DataIn");
        var now = SiteClock.Now;
        Assert.True(Directory.Exists(Path.Combine(w.DoneBase, now.Year.ToString(), $"{now.Year}-{now.Month:D2}", "E1T2__DONE")), "Task 2: DONE folder archived");
        Assert.False(File.Exists(Path.Combine(w.RawDir, "E1T3__.sch")), "Task 3: SCH deleted");
        Assert.True(File.Exists(Path.Combine(w.Archive, "E1T4__.MPS")), "Task 4: MPS archived");
        Assert.False(File.Exists(Path.Combine(w.DataIn, "E1T4__.MPS")));

        var jobs = await _f.WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.JobRef.StartsWith("E1T")).ToDictionaryAsync(j => j.JobRef));
        Assert.NotNull(jobs["E1T3"].SchDeletedAt);
        Assert.NotNull(jobs["E1T4"].MpsArchivedAt);

        var logs = await _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().ToListAsync());
        Assert.Contains(logs, l => l.TaskName == "MpsToDatIn" && l.JobRef == "E1T1" && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "SawFilesCleanup" && l.SourcePath!.Contains("E1T2__DONE") && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "SchDelete" && l.JobRef == "E1T3" && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "MpsArchive" && l.JobRef == "E1T4" && l.Outcome == "success");
        Assert.Contains(logs, l => l.TaskName == "RetentionArchive" && l.Outcome == "success");
    }

    [Fact]
    public async Task ATaskThatThrows_IsLoggedAsAnError_AndDoesNotBlockTheOthers()
    {
        // Task 2's archive root is an impossible path, so it throws while creating the destination.
        var w = await BuildWorldAsync("2", doneBaseOverride: Path.Combine(_f.TempRoot, "bad|archive:root"));
        await SeedAllFourTriggersAsync(w, "2");

        await _f.RunNightlyAsync();

        var logs = await _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().ToListAsync());
        Assert.Contains(logs, l => l.TaskName.StartsWith("Task 2") && l.Outcome == "error");

        // Tasks 1, 3, 4 and retention all still ran to completion.
        Assert.True(File.Exists(Path.Combine(w.DataIn, "E2T1__.MPS")));
        Assert.False(File.Exists(Path.Combine(w.RawDir, "E2T3__.sch")));
        Assert.True(File.Exists(Path.Combine(w.Archive, "E2T4__.MPS")));
        Assert.Contains(logs, l => l.TaskName == "RetentionArchive" && l.Outcome == "success");
        Assert.True(Directory.Exists(Path.Combine(w.ForCutting, "E2T2__DONE")), "the DONE folder must be left in place, not lost");
    }
}
