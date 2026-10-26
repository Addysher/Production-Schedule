using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §9.4 gaps closed in v3.0: late completions counted properly, member-level progress on the Dashboard,
// and archived jobs listable by Admins.
[Collection(IntegrationCollection.Name)]
public class ReportingGapTests
{
    private readonly IntegrationFixture _f;

    public ReportingGapTests(IntegrationFixture fixture) => _f = fixture;

    private static Job Finished(string jobRef, int stationId, DateOnly due, DateTime finishedAt, bool archived = false) => new()
    {
        JobRef = jobRef, ComponentType = "GapProduct", DesignStatus = "FinalDesign", Status = "completed", TotalEqA = 5,
        EstBookedDeliveryDate = due, AssignedStationId = stationId, IsArchived = archived,
        CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
        ScheduleItems =
        [
            new ScheduleItem
            {
                JobRef = jobRef, StationId = stationId, ScheduleDate = DateOnly.FromDateTime(finishedAt), PlannedStart = finishedAt.AddHours(-1),
                PlannedEnd = finishedAt, ActualStart = finishedAt.AddHours(-1), ActualEnd = finishedAt, GeneratedAt = SiteClock.Now, IsArchived = archived,
            },
        ],
    };

    [Fact]
    public async Task CompletionsReport_CountsJobsThatFinishedAfterTheirDueDate_IncludingArchivedOnes()
    {
        var stationId = await _f.AddStationAsync("Gap Line", "GapProduct");
        var day = DateOnly.FromDateTime(SiteClock.Now).AddDays(-20);
        var at = day.ToDateTime(new TimeOnly(10, 0));
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.AddRange(
                Finished("GAP-ONTIME", stationId, day, at),
                Finished("GAP-LATE", stationId, day.AddDays(-2), at, archived: true));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var rows = await admin.GetFromJsonAsync<JsonElement>($"/api/reports/completions?dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}");

        var row = Assert.Single(rows.EnumerateArray());
        Assert.Equal(2, row.GetProperty("completed").GetInt32());
        Assert.Equal(1, row.GetProperty("completedLate").GetInt32());
        Assert.Equal(50, row.GetProperty("latePercent").GetInt32());
        Assert.Equal("GAP-LATE", Assert.Single(row.GetProperty("lateJobRefs").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task StationProgress_ShowsTheMemberBeingMadeAndTheNextOne()
    {
        var stationId = await _f.AddStationAsync("Gap Run Line", "GapRun");
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(new Job
            {
                JobRef = "GAP-RUN", ComponentType = "GapRun", DesignStatus = "FinalDesign", Status = "in-production", TotalEqA = 3,
                AssignedStationId = stationId, CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
                Members =
                [
                    new JobMember { JobRef = "GAP-RUN", MemberId = "E03", Batch = 1, Sequence = 3, EqaValue = 1 },
                    new JobMember { JobRef = "GAP-RUN", MemberId = "E01", Batch = 1, Sequence = 1, EqaValue = 1, Done = true },
                    new JobMember { JobRef = "GAP-RUN", MemberId = "E02", Batch = 1, Sequence = 2, EqaValue = 1 },
                ],
                ScheduleItems =
                [
                    new ScheduleItem
                    {
                        JobRef = "GAP-RUN", StationId = stationId, ScheduleDate = DateOnly.FromDateTime(SiteClock.Now), PlannedStart = SiteClock.Now,
                        PlannedEnd = SiteClock.Now.AddHours(1), ActualStart = SiteClock.Now, GeneratedAt = SiteClock.Now,
                    },
                ],
            });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var progress = (await admin.GetFromJsonAsync<JsonElement>("/api/schedule/station-progress")).EnumerateArray().Single(p => p.GetProperty("jobRef").GetString() == "GAP-RUN");

        Assert.Equal("E02", progress.GetProperty("currentMemberId").GetString());
        Assert.Equal("E03", progress.GetProperty("nextMemberId").GetString());
        Assert.Equal(1, progress.GetProperty("membersDone").GetInt32());
        Assert.Equal(3, progress.GetProperty("membersTotal").GetInt32());
    }

    [Fact]
    public async Task ArchivedJobs_AreListedOnlyWhenAnAdminAsks()
    {
        var stationId = await _f.AddStationAsync("Gap Arch Line", "GapProduct");
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(Finished("GAP-ARCH", stationId, DateOnly.FromDateTime(SiteClock.Now), SiteClock.Now.AddDays(-40), archived: true));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var live = await admin.GetFromJsonAsync<JsonElement>("/api/jobs");
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/jobs?includeArchived=true");

        Assert.DoesNotContain(live.EnumerateArray(), j => j.GetProperty("jobRef").GetString() == "GAP-ARCH");
        Assert.True(all.EnumerateArray().Single(j => j.GetProperty("jobRef").GetString() == "GAP-ARCH").GetProperty("isArchived").GetBoolean());

        await admin.PostAsJsonAsync("/api/users", new { username = "gap_viewer", password = "Pass1234!", role = "Viewer" });
        var viewer = await _f.ClientAsync("gap_viewer", "Pass1234!");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/jobs?includeArchived=true")).StatusCode);
    }
}
