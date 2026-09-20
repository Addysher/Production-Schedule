using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionScheduler.Domain;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Pre-live-test hardening: the readers follow the factory's clock, and input they can't understand is
// rejected or reported — visibly, in MaintenanceLogs — instead of being dropped or half-imported.
[Collection(IntegrationCollection.Name)]
public class SiteTimeAndInputHardeningTests
{
    private readonly IntegrationFixture _f;

    public SiteTimeAndInputHardeningTests(IntegrationFixture fixture) => _f = fixture;

    private PrdReaderService PrdReaderAt(DateTime siteNow) =>
        new(_f.ScopeFactory, NullLogger<PrdReaderService>.Instance, () => siteNow);

    private async Task<int> AddCutJobAsync(string jobRef, string comp, string dataIn, params string[] members)
    {
        var stationId = await _f.AddStationAsync($"Station {jobRef}", comp, dataIn);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(new Job
            {
                JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "cut", AssignedStationId = stationId,
                CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
            });
            await db.SaveChangesAsync();
            foreach (var m in members)
                db.JobMembers.Add(new JobMember { JobRef = jobRef, MemberId = m, ComponentType = comp, Batch = 1, Sequence = 1, SubSequence = 1, EqaValue = 10 });
            await db.SaveChangesAsync();
        });
        return stationId;
    }

    private Task<List<MaintenanceLog>> LogsAsync(string taskName) =>
        _f.WithDbAsync(db => db.MaintenanceLogs.AsNoTracking().Where(l => l.TaskName == taskName).ToListAsync());

    [Fact]
    public async Task PrdReader_OpensTheFileNamedForTheSiteDate_AndKeepsEventTimesAsWritten()
    {
        var dataIn = _f.NewDir("tzdatain");
        await AddCutJobAsync("TZ1", "ScnTz", dataIn, "M1", "M2");
        var siteNow = new DateTime(2031, 3, 4, 6, 0, 0); // 06:00 site time — still "yesterday" in UTC for a UTC+10 factory

        await File.WriteAllLinesAsync(Path.Combine(dataIn, "20310304.prd"), new[]
        {
            "Version,1.0.0.7,PaperlessJig,05:00,Station TZ1,EqA,Mbrs",
            "Trusses Made,TZ1,M1,2031-03-04 05:30:00,1,0,0,10.00",
        });
        // A decoy named for the real UTC date must be ignored while the site clock says 2031-03-04.
        await File.WriteAllLinesAsync(Path.Combine(dataIn, $"{DateTime.UtcNow:yyyyMMdd}.prd"), new[]
        {
            "Version,1.0.0.7,PaperlessJig,05:00,Station TZ1,EqA,Mbrs",
            "Trusses Made,TZ1,M2,2031-03-04 05:31:00,1,0,0,10.00",
        });

        await PrdReaderAt(siteNow).PollAsync(default);

        var members = await _f.WithDbAsync(db => db.JobMembers.AsNoTracking().Where(m => m.JobRef == "TZ1").ToDictionaryAsync(m => m.MemberId));
        Assert.True(members["M1"].Done);
        Assert.Equal(new DateTime(2031, 3, 4, 5, 30, 0), members["M1"].CompletedAt);
        Assert.False(members["M2"].Done);
    }

    [Fact]
    public async Task PrdReader_ALineAppendedAfterATrailingNewline_IsNotSkipped()
    {
        var dataIn = _f.NewDir("appenddatain");
        await AddCutJobAsync("AP1", "ScnAp", dataIn, "M1", "M2");
        var siteNow = new DateTime(2032, 1, 2, 9, 0, 0);
        var file = Path.Combine(dataIn, "20320102.prd");

        await File.WriteAllTextAsync(file, "Version,1.0.0.7,PaperlessJig,05:00,S,EqA,Mbrs\nTrusses Made,AP1,M1,2032-01-02 08:00:00,1,0,0,10.00\n");
        await PrdReaderAt(siteNow).PollAsync(default);
        await File.AppendAllTextAsync(file, "Trusses Made,AP1,M2,2032-01-02 08:30:00,1,0,0,10.00\n");
        await PrdReaderAt(siteNow).PollAsync(default);

        var job = await _f.WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.JobRef == "AP1"));
        Assert.Equal("completed", job.Status);
    }

    [Fact]
    public async Task PrdReader_EverythingItSkipsOrDoubtsIsWrittenToTheMaintenanceLog()
    {
        var dataIn = _f.NewDir("diagdatain");
        await AddCutJobAsync("DG1", "ScnDg", dataIn, "M1");
        var siteNow = new DateTime(2033, 5, 6, 8, 0, 0);

        await File.WriteAllTextAsync(Path.Combine(dataIn, "20330506.prd"), string.Join("\n", new[]
        {
            "Version,1.0.0.7,PaperlessJig,05:00,S,EqA,Mbrs",
            "Truss Exploded,DG1,M1,2033-05-06 07:00:00",                    // an event name we've never heard of
            "Trusses Made,DG1,M1,2033-05-06 07:01:00",                      // too few fields
            "Trusses Made,DG1,M1,2033-05-06 07:02:00,1,0,0,lots",           // EqA isn't a number
            "Trusses Made,DG1,ZZ9,2033-05-06 07:03:00,1,0,0,10.00",         // no such member
            "Trusses Made,DG1,M1,not-a-time,1,0,0,10.00",                   // real member, unreadable time
        }) + "\n");

        await PrdReaderAt(siteNow).PollAsync(default);

        var logs = await LogsAsync("PrdRead");
        Assert.Contains(logs, l => l.Outcome == "skipped" && l.Message!.Contains("Truss Exploded"));
        Assert.Contains(logs, l => l.Outcome == "error" && l.Message!.Contains("Line 3") && l.Message.Contains("expected 8"));
        Assert.Contains(logs, l => l.Outcome == "error" && l.Message!.Contains("Line 4") && l.Message.Contains("'lots'"));
        Assert.Contains(logs, l => l.Outcome == "skipped" && l.Message!.Contains("DG1/ZZ9"));
        Assert.Contains(logs, l => l.Outcome == "error" && l.Message!.Contains("Line 6") && l.Message.Contains("not-a-time"));

        // The one usable event still counted, using the read time for its unreadable timestamp.
        var member = await _f.WithDbAsync(db => db.JobMembers.AsNoTracking().SingleAsync(m => m.JobRef == "DG1" && m.MemberId == "M1"));
        Assert.True(member.Done);
        Assert.Equal(siteNow, member.CompletedAt);
    }

    private async Task<string> NewInboxAsync()
    {
        var inbox = _f.NewDir("hardinbox");
        await _f.WithDbAsync(async db =>
        {
            db.SchImportPaths.Add(new SchImportPath { Name = "hardening " + Path.GetFileName(inbox), UncPath = inbox, IsActive = true });
            await db.SaveChangesAsync();
        });
        return inbox;
    }

    [Fact]
    public async Task SchImport_RejectsAFileWithAProblemLine_SaysWhy_AndImportsNothing()
    {
        var inbox = await NewInboxAsync();
        var file = Path.Combine(inbox, "HRD1__.sch");
        await File.WriteAllLinesAsync(file, new[]
        {
            ScenarioA_FullJobLifecycleTests.ScheduleJobLine("HRD1", "ScnHard", "finaldesign", "02/09/2026"),
            ScenarioA_FullJobLifecycleTests.ScheduleEntryLine("HRD1", "ScnHard", "E17", 60m),
            ScenarioA_FullJobLifecycleTests.ScheduleEntryLine("HRD1", "ScnHard", "I31", 40m).Replace(";40", ";forty"),
        });

        await _f.PollSchImportAsync();

        Assert.False(await _f.WithDbAsync(db => db.Jobs.AnyAsync(j => j.JobRef == "HRD1")), "a partly-readable file must not create a job");
        Assert.False(File.Exists(file));
        Assert.True(File.Exists(Path.Combine(inbox, "_errors", "HRD1__.sch")));
        var log = Assert.Single(await LogsAsync("SchImport"), l => l.Outcome == "error" && l.SourcePath?.Contains("HRD1") == true);
        Assert.Contains("line 3", log.Message);
        Assert.Contains("forty", log.Message);
    }

    [Fact]
    public async Task SchImport_LeavesAFileAloneUntilItHasStoppedChanging()
    {
        var inbox = await NewInboxAsync();
        var file = Path.Combine(inbox, "HRD2__.sch");
        await File.WriteAllLinesAsync(file, new[]
        {
            ScenarioA_FullJobLifecycleTests.ScheduleJobLine("HRD2", "ScnHard", "finaldesign", "02/09/2026"),
            ScenarioA_FullJobLifecycleTests.ScheduleEntryLine("HRD2", "ScnHard", "E17", 60m),
        });

        try
        {
            await _f.SetSettingsAsync(("SchFileSettleSeconds", "600")); // the file is brand new, so "still being written"
            await _f.PollSchImportAsync();
            Assert.True(File.Exists(file), "a fresh file should be left in the inbox");
            Assert.False(await _f.WithDbAsync(db => db.Jobs.AnyAsync(j => j.JobRef == "HRD2")));
        }
        finally
        {
            await _f.SetSettingsAsync(("SchFileSettleSeconds", "0"));
        }

        await _f.PollSchImportAsync();
        Assert.True(await _f.WithDbAsync(db => db.Jobs.AnyAsync(j => j.JobRef == "HRD2")));
    }

    [Fact]
    public async Task SchImport_SuccessIsAlsoLogged_SoTheDashboardShowsIt()
    {
        var inbox = await NewInboxAsync();
        await File.WriteAllLinesAsync(Path.Combine(inbox, "HRD3__.sch"), new[]
        {
            ScenarioA_FullJobLifecycleTests.ScheduleJobLine("HRD3", "ScnHard", "quote", "02/09/2026"),
            ScenarioA_FullJobLifecycleTests.ScheduleEntryLine("HRD3", "ScnHard", "E17", 60m),
        });

        await _f.PollSchImportAsync();

        var log = Assert.Single(await LogsAsync("SchImport"), l => l.JobRef == "HRD3");
        Assert.Equal("success", log.Outcome);
        Assert.Contains("1 members", log.Message);
    }
}
