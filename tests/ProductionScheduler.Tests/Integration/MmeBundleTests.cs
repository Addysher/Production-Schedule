using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §5.11 v3.0: saw files are collected in a staging folder and only reach the waiting folder once every
// member has its file; approval stays manual.
[Collection(IntegrationCollection.Name)]
public class MmeBundleTests
{
    private readonly IntegrationFixture _f;

    public MmeBundleTests(IntegrationFixture fixture) => _f = fixture;

    private sealed record Folders(string Raw, string Staging, string Waiting, string Cutting);

    private async Task<Folders> UseFreshFoldersAsync()
    {
        var raw = _f.NewDir("mme-raw");
        var folders = new Folders(raw, Path.Combine(raw, "_bundling"), _f.NewDir("mme-waiting"), _f.NewDir("mme-cutting"));
        await _f.SetSettingsAsync(("RawFilesPath", folders.Raw), ("SawFilesDoNotCutPath", folders.Waiting), ("SawFilesForCuttingPath", folders.Cutting));
        return folders;
    }

    private static string NewRef() => "M" + Guid.NewGuid().ToString("N")[..7].ToUpperInvariant();

    private Task AddJobAsync(string jobRef, params string[] memberIds) => _f.WithDbAsync(async db =>
    {
        db.Jobs.Add(new Job
        {
            JobRef = jobRef, ComponentType = "MmeProduct", DesignStatus = "FinalDesign", Status = "ready", TotalEqA = 10,
            BookedDeliveryDate = SiteClock.Today.AddDays(30), // called up
            CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
            Members = memberIds.Select((id, i) => new JobMember { JobRef = jobRef, MemberId = id, EqaValue = 5, Sequence = i + 1 }).ToList(),
        });
        await db.SaveChangesAsync();
    });

    private static Task DropAsync(Folders f, string jobRef, string memberId) =>
        File.WriteAllTextAsync(Path.Combine(f.Raw, $"{jobRef}{memberId}.mme"), "cut list");

    private Task<Job> JobAsync(string jobRef) => _f.WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.JobRef == jobRef));

    [Fact]
    public async Task APartBundle_WaitsInStaging_AndMovesToTheWaitingFolderWhenComplete()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17", "I31");

        await DropAsync(f, jobRef, "E17");
        await _f.PollMmeAsync();
        Assert.True(File.Exists(Path.Combine(f.Staging, jobRef, $"{jobRef}E17.mme")));
        Assert.False(Directory.Exists(Path.Combine(f.Waiting, jobRef)));
        Assert.Equal("bundling", (await JobAsync(jobRef)).SawBundleStatus);

        await DropAsync(f, jobRef, "I31");
        await _f.PollMmeAsync();
        Assert.Equal(2, Directory.GetFiles(Path.Combine(f.Waiting, jobRef)).Length);
        Assert.False(Directory.Exists(Path.Combine(f.Staging, jobRef)));
        var job = await JobAsync(jobRef);
        Assert.Equal("waiting", job.SawBundleStatus);
        Assert.Equal(2, job.SawFileCount);
    }

    [Fact]
    public async Task SawFilesThatArriveBeforeTheDesign_AreHeld_UntilTheJobAndItsMembersExist()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await DropAsync(f, jobRef, "E17");
        await _f.PollMmeAsync();
        Assert.True(File.Exists(Path.Combine(f.Staging, jobRef, $"{jobRef}E17.mme")));

        await AddJobAsync(jobRef, "E17");
        await _f.PollMmeAsync();

        Assert.True(File.Exists(Path.Combine(f.Waiting, jobRef, $"{jobRef}E17.mme")));
        Assert.Equal("waiting", (await JobAsync(jobRef)).SawBundleStatus);
    }

    [Fact]
    public async Task ApprovingAnIncompleteBundle_NeedsConfirmation()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17", "I31");
        await DropAsync(f, jobRef, "E17");
        await _f.PollMmeAsync();
        var admin = await _f.ClientAsync();

        var first = await admin.PostAsync($"/api/jobs/{jobRef}/approve-for-cutting", null);
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Contains("incomplete", await first.Content.ReadAsStringAsync());
        Assert.Equal("ready", (await JobAsync(jobRef)).Status);

        var forced = await admin.PostAsync($"/api/jobs/{jobRef}/approve-for-cutting?force=true", null);
        Assert.Equal(HttpStatusCode.OK, forced.StatusCode);
        Assert.True(File.Exists(Path.Combine(f.Cutting, jobRef, $"{jobRef}E17.mme")));
        var job = await JobAsync(jobRef);
        Assert.Equal("approved", job.Status);
        Assert.Equal("cutting", job.SawBundleStatus);
    }

    [Fact]
    public async Task ApprovingWithNoSawFiles_IsRefusedWithAReason()
    {
        await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17");
        var admin = await _f.ClientAsync();

        var response = await admin.PostAsync($"/api/jobs/{jobRef}/approve-for-cutting", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("No saw files", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AFileForAnAlreadyApprovedJob_IsHeldBackAndFlagged()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17");
        await DropAsync(f, jobRef, "E17");
        await _f.PollMmeAsync();
        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/jobs/{jobRef}/approve-for-cutting", null)).StatusCode);

        await DropAsync(f, jobRef, "E17"); // a revised file after approval
        await _f.PollMmeAsync();

        Assert.True(File.Exists(Path.Combine(f.Staging, jobRef, $"{jobRef}E17.mme")));
        Assert.Equal("cutting", (await JobAsync(jobRef)).SawBundleStatus);
        Assert.True(await _f.WithDbAsync(db => db.MaintenanceLogs.AnyAsync(l => l.TaskName == "MmeBundle" && l.JobRef == jobRef && l.Outcome == "error")));
    }

    [Fact]
    public async Task ABadlyNamedFile_IsSetAside_AndTheRestStillBundle()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17");
        await File.WriteAllTextAsync(Path.Combine(f.Raw, "AB.mme"), "?");
        await DropAsync(f, jobRef, "E17");

        await _f.PollMmeAsync();

        Assert.True(File.Exists(Path.Combine(f.Raw, "_errors", "AB.mme")));
        Assert.True(File.Exists(Path.Combine(f.Waiting, jobRef, $"{jobRef}E17.mme")));
    }

    [Fact]
    public async Task AMemberWhoseIdIsntThreeCharacters_IsCountedNotMatched()
    {
        // Real floor designs list members like P05A; the saw file can only carry 3 characters, so such a
        // member needs one more file than the 3-character members account for.
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "P05", "P05A");
        await DropAsync(f, jobRef, "P05");
        await _f.PollMmeAsync();
        Assert.Equal("bundling", (await JobAsync(jobRef)).SawBundleStatus);

        await DropAsync(f, jobRef, "P5A");
        await _f.PollMmeAsync();

        Assert.Equal(2, Directory.GetFiles(Path.Combine(f.Waiting, jobRef)).Length);
        Assert.Equal("waiting", (await JobAsync(jobRef)).SawBundleStatus);
    }

    [Fact]
    public async Task ABundleAlreadyInTheWaitingFolder_ShowsItsRealState_AndAPartOneNeedsConfirming()
    {
        // As left by the bundler before jobs tracked their bundle: files straight in the waiting folder.
        var f = await UseFreshFoldersAsync();
        var whole = NewRef();
        var part = NewRef();
        await AddJobAsync(whole, "E17", "I31");
        await AddJobAsync(part, "E17", "I31");
        foreach (var (jobRef, ids) in new[] { (whole, new[] { "E17", "I31" }), (part, new[] { "E17" }) })
        {
            Directory.CreateDirectory(Path.Combine(f.Waiting, jobRef));
            foreach (var id in ids) await File.WriteAllTextAsync(Path.Combine(f.Waiting, jobRef, $"{jobRef}{id}.mme"), "cut list");
        }

        await _f.PollMmeAsync();

        Assert.Equal("waiting", (await JobAsync(whole)).SawBundleStatus);
        Assert.Equal("bundling", (await JobAsync(part)).SawBundleStatus);
        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/jobs/{part}/approve-for-cutting", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/jobs/{whole}/approve-for-cutting", null)).StatusCode);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(f.Cutting, whole)).Length);
        Assert.False(Directory.Exists(Path.Combine(f.Waiting, whole)));
    }

    [Fact]
    public async Task TheJobsList_ShowsWhereEachBundleIs()
    {
        var f = await UseFreshFoldersAsync();
        var jobRef = NewRef();
        await AddJobAsync(jobRef, "E17", "I31");
        await DropAsync(f, jobRef, "E17");
        await _f.PollMmeAsync();
        var admin = await _f.ClientAsync();

        var jobs = await admin.GetFromJsonAsync<JsonElement>("/api/jobs");
        var summary = jobs.EnumerateArray().Single(j => j.GetProperty("jobRef").GetString() == jobRef);
        Assert.Equal("bundling", summary.GetProperty("sawBundleStatus").GetString());
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/jobs/{summary.GetProperty("id").GetInt32()}");
        Assert.Equal(1, detail.GetProperty("sawFileCount").GetInt32());
        Assert.Equal(2, detail.GetProperty("memberCount").GetInt32());
    }
}
