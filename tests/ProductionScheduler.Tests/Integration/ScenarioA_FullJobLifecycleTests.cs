using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario A: quote SCH -> finaldesign SCH -> MME bundled -> Admin approves -> DONE
// folder detected -> PRD Trusses Made events -> completed -> nightly tasks clean up.
[Collection(IntegrationCollection.Name)]
public class ScenarioA_FullJobLifecycleTests
{
    private readonly IntegrationFixture _f;

    public ScenarioA_FullJobLifecycleTests(IntegrationFixture fixture) => _f = fixture;

    // Prefixed with the record-type label real Sapphire exports carry on every line — see
    // SchImportService.ParseFile, whose real-world checks this shared fixture also feeds.
    internal static string ScheduleJobLine(string jobRef, string comp, string designStatus, string jobOrderDate) =>
        $"ScheduleJob: {jobRef};?;{comp};Test Homes;{Due()};{Due()};0;2;38477;1 Example St;Testville;;;{jobOrderDate};09:15:00;{designStatus}";

    // `eqa` lands on field 14 (ProcessEqA) with field 13 (SetupEqA) fixed at 0, so EqaValue == eqa —
    // see SchImportService.ParseFile's real-world notes on why field 23 (left as an inert 0.00 here)
    // isn't the production value.
    internal static string ScheduleEntryLine(string jobRef, string comp, string memberId, decimal eqa, int sequence = 1) =>
        $"ScheduleEntry: {jobRef};?;{comp};{memberId};x;x;x;Ext Load Bearing;x;x;x;x;0.00;{eqa};x;x;3200;1800;x;x;x;False;0.00;x;1;{sequence};1";

    private static string Due() => SiteClock.Now.AddDays(14).ToString("dd/MM/yyyy");

    private async Task<Job> LoadJobAsync(string jobRef) =>
        await _f.WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.JobRef == jobRef));

    [Fact]
    public async Task QuoteToCompletion_DrivesEveryTransition_AndNightlyTasksCleanUp()
    {
        const string comp = "ScnA";
        var jobRef = "A" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();
        var raw = _f.NewDir("raw");
        var doNotCut = _f.NewDir("donotcut");
        var forCutting = _f.NewDir("forcutting");
        var doneBase = _f.NewDir("donebase");
        var schInbox = _f.NewDir("schinbox");
        var dataIn = _f.NewDir("datain");
        var archive = _f.NewDir("archive");

        await _f.SetSettingsAsync(
            ("RawFilesPath", raw), ("SawFilesDoNotCutPath", doNotCut), ("SawFilesForCuttingPath", forCutting),
            ("SawFilesDoneBasePath", doneBase), ("MpsPreMoveDaysBeforeProduction", "1"), ("ScheduleRetentionMonths", "6"));
        var stationId = await _f.AddStationAsync("ScnA Station", comp, dataIn, archive);
        await _f.WithDbAsync(async db =>
        {
            db.SchImportPaths.Add(new SchImportPath { Name = "ScnA inbox", UncPath = schInbox, ComponentType = comp, IsActive = true });
            db.BatchcutWatchPaths.Add(new BatchcutWatchPath { Name = "ScnA for cutting", UncPath = forCutting, IsActive = true });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();
        var schFile = Path.Combine(schInbox, $"{jobRef}__.sch");

        // 1. Quote SCH -> job created as 'scheduled', JobOrderDate recorded.
        await File.WriteAllLinesAsync(schFile, new[]
        {
            ScheduleJobLine(jobRef, comp, "quote", "02/09/2026"),
            ScheduleEntryLine(jobRef, comp, "E17", 60m),
            ScheduleEntryLine(jobRef, comp, "I31", 40m),
        });
        await _f.PollSchImportAsync();
        var job = await LoadJobAsync(jobRef);
        Assert.Equal("scheduled", job.Status);
        Assert.Equal("Quote", job.DesignStatus);
        Assert.Equal(new DateOnly(2026, 9, 2), job.JobOrderDate);
        Assert.Equal(100m, job.TotalEqA);

        // 2. finaldesign SCH (deliberately different JobOrderDate) -> 'ready', dates preserved, scheduled.
        await File.WriteAllLinesAsync(schFile, new[]
        {
            ScheduleJobLine(jobRef, comp, "finaldesign", "01/01/2000"),
            ScheduleEntryLine(jobRef, comp, "E17", 60m),
            ScheduleEntryLine(jobRef, comp, "I31", 40m),
        });
        await _f.PollSchImportAsync();
        job = await LoadJobAsync(jobRef);
        Assert.Equal("ready", job.Status);
        Assert.Equal("FinalDesign", job.DesignStatus);
        Assert.Equal(new DateOnly(2026, 9, 2), job.JobOrderDate);
        Assert.Equal(stationId, job.AssignedStationId);
        Assert.NotNull(job.EstimatedStartDate);
        var items = await _f.WithDbAsync(db => db.ScheduleItems.AsNoTracking().Where(i => i.JobRef == jobRef).ToListAsync());
        Assert.Equal(2, Assert.Single(items).MembersTotal);

        // 3. MME files land in Raw Files -> bundled into DO NOT CUT. (An MPS file sits alongside them.)
        await File.WriteAllTextAsync(Path.Combine(raw, $"{jobRef}E17.mme"), "cut list");
        await File.WriteAllTextAsync(Path.Combine(raw, $"{jobRef}I31.mme"), "cut list");
        await File.WriteAllTextAsync(Path.Combine(raw, $"{jobRef}__.MPS"), "mps");
        await _f.PollMmeAsync();
        Assert.True(File.Exists(Path.Combine(doNotCut, jobRef, $"{jobRef}E17.mme")));
        Assert.True(File.Exists(Path.Combine(doNotCut, jobRef, $"{jobRef}I31.mme")));

        // 3b. Nightly Task 1 pre-moves the MPS into the station's DataIn the day before production.
        await _f.WithDbAsync(async db =>
        {
            var tracked = await db.Jobs.SingleAsync(j => j.JobRef == jobRef);
            tracked.EstimatedStartDate = SiteClock.Now.Date.AddDays(1);
            await db.SaveChangesAsync();
        });
        await _f.RunNightlyAsync();
        Assert.True(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));

        // 4. Admin approves for cutting -> folder moves, status 'approved', calendar shows it.
        var approve = await admin.PostAsync($"/api/jobs/{jobRef}/approve-for-cutting", null);
        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        Assert.False(Directory.Exists(Path.Combine(doNotCut, jobRef)));
        Assert.True(Directory.Exists(Path.Combine(forCutting, jobRef)));
        Assert.Equal("approved", (await LoadJobAsync(jobRef)).Status);
        var calendar = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        Assert.Contains(calendar.EnumerateArray(), i => i.GetProperty("jobRef").GetString() == jobRef
                                                       && i.GetProperty("jobStatus").GetString() == "approved");

        // 5. Operator renames the folder to {JobRef}__DONE -> batchcut watcher moves it to 'cut'.
        Directory.Move(Path.Combine(forCutting, jobRef), Path.Combine(forCutting, $"{jobRef}__DONE"));
        await _f.PollBatchcutAsync();
        Assert.Equal("cut", (await LoadJobAsync(jobRef)).Status);
        var events = await _f.WithDbAsync(db => db.BatchcutEvents.AsNoTracking().Where(e => e.JobRef == jobRef).ToListAsync());
        Assert.Equal("applied", Assert.Single(events).Status);

        // 6. PRD: first Trusses Made -> 'in-production'; last one -> 'completed'.
        var prdFile = Path.Combine(dataIn, $"{SiteClock.Now:yyyyMMdd}.prd");
        string Stamp(int minutesAgo) => SiteClock.Now.AddMinutes(-minutesAgo).ToString("yyyy-MM-dd HH:mm:ss");
        await File.WriteAllLinesAsync(prdFile, new[]
        {
            "Version,1.0.0.7,PaperlessJig,05:00,ScnA Station,EqA,Mbrs",
            $"Trusses Made,{jobRef},E17,{Stamp(10)},1,0,0,60.00",
        });
        await _f.PollPrdAsync();
        Assert.Equal("in-production", (await LoadJobAsync(jobRef)).Status);

        await File.AppendAllTextAsync(prdFile, $"Trusses Made,{jobRef},I31,{Stamp(5)},1,0,0,40.00{Environment.NewLine}");
        await _f.PollPrdAsync();
        job = await LoadJobAsync(jobRef);
        Assert.Equal("completed", job.Status);
        Assert.Equal(100m, job.CompletedEqA);
        var item = await _f.WithDbAsync(db => db.ScheduleItems.AsNoTracking().SingleAsync(i => i.JobRef == jobRef));
        Assert.NotNull(item.ActualStart);
        Assert.NotNull(item.ActualEnd);
        Assert.Equal(2, item.MembersCompleted);
        Assert.Equal(100m, item.EqaCompleted);

        // 7. Nightly tasks after completion: SCH deleted, MPS archived, DONE folder archived, all logged.
        var schPathBefore = job.SchSourcePath!;
        Assert.True(File.Exists(schPathBefore), "SchSourcePath should point at the real (moved) SCH file so Task 3 can delete it.");
        await _f.RunNightlyAsync();

        job = await LoadJobAsync(jobRef);
        Assert.False(File.Exists(schPathBefore));
        Assert.NotNull(job.SchDeletedAt);
        Assert.False(File.Exists(Path.Combine(dataIn, $"{jobRef}__.MPS")));
        Assert.True(File.Exists(Path.Combine(archive, $"{jobRef}__.MPS")));
        Assert.NotNull(job.MpsArchivedAt);
        var now = SiteClock.Now;
        Assert.True(Directory.Exists(Path.Combine(doneBase, now.Year.ToString(), $"{now.Year}-{now.Month:D2}", $"{jobRef}__DONE")));
        Assert.False(Directory.Exists(Path.Combine(forCutting, $"{jobRef}__DONE")));

        var logs = await _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().Where(l => l.JobRef == jobRef || l.SourcePath!.Contains(jobRef)).ToListAsync());
        foreach (var task in new[] { "MpsToDatIn", "SchDelete", "MpsArchive", "SawFilesCleanup" })
        {
            Assert.Contains(logs, l => l.TaskName == task && l.Outcome == "success");
        }
    }
}
