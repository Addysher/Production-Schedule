using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario C: slower-than-planned Trusses Made events -> creep flag fires at the
// threshold -> the scheduler holds the job (and everything queued behind it) until an Admin
// approves -> approval clears the alert and the scheduler recalculates from reality.
[Collection(IntegrationCollection.Name)]
public class ScenarioC_CreepDetectionTests
{
    private readonly IntegrationFixture _f;

    public ScenarioC_CreepDetectionTests(IntegrationFixture fixture) => _f = fixture;

    private static DateTime Dt(JsonElement e, string name) => e.GetProperty(name).GetDateTime();

    private async Task<JsonElement> ItemFor(HttpClient admin, string jobRef)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Single(i => i.GetProperty("jobRef").GetString() == jobRef);
    }

    [Fact]
    public async Task Creep_FlagsAtThreshold_HoldsScheduleUntilApproved_ThenRecalculates()
    {
        const string comp = "ScnC";
        var dataIn = _f.NewDir("cdatain");
        var stationId = await _f.AddStationAsync("ScnC Station", comp, dataIn);
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // 6 min per EqA: job X (100 EqA) = 10h planned; it started 6h ago and should end in 4h.
        await _f.WithDbAsync(async db =>
        {
            db.JobTypes.Add(new JobType { Name = "ScnC unit", ComponentType = comp, MinsPerUnit = 6, IsActive = true });
            db.Jobs.Add(new Job
            {
                JobRef = "CX", ComponentType = comp, DesignStatus = "FinalDesign", Status = "cut", TotalEqA = 100,
                AssignedStationId = stationId, EstBookedDeliveryDate = today.AddDays(30), ExpectedDeliveryDate = today.AddDays(30),
                EstimatedStartDate = now.AddHours(-6), EstimatedCompletionDate = now.AddHours(4), CreatedAt = now, UpdatedAt = now,
            });
            db.Jobs.Add(new Job
            {
                JobRef = "CY", ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = 60,
                EstBookedDeliveryDate = today.AddDays(30), ExpectedDeliveryDate = today.AddDays(30), CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            foreach (var m in new[] { "M1", "M2", "M3", "M4" })
                db.JobMembers.Add(new JobMember { JobRef = "CX", MemberId = m, ComponentType = comp, Batch = 1, Sequence = 1, SubSequence = 1, EqaValue = 25 });
            db.ScheduleItems.Add(new ScheduleItem
            {
                JobRef = "CX", StationId = stationId, ScheduleDate = DateOnly.FromDateTime(now.AddHours(-6)),
                PlannedStart = now.AddHours(-6), PlannedEnd = now.AddHours(4), PlannedEqA = 100, MembersTotal = 4, GeneratedAt = now,
            });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        // Only 25 of the 50 EqA that should exist after 5 hours are done -> 50% behind, threshold 20%.
        await File.WriteAllLinesAsync(Path.Combine(dataIn, $"{DateTime.UtcNow:yyyyMMdd}.prd"), new[]
        {
            "Version,1.0.0.7,PaperlessJig,05:00,ScnC Station,EqA,Mbrs",
            $"Trusses Made,CX,M1,{now.AddHours(-5):yyyy-MM-dd HH:mm:ss},1,0,0,25.00",
        });
        await _f.PollPrdAsync();

        var flagged = await ItemFor(admin, "CX");
        Assert.True(flagged.GetProperty("creepDetected").GetBoolean(), "creep should fire at 50% behind plan");
        Assert.Equal(JsonValueKind.Null, flagged.GetProperty("creepApprovedAt").ValueKind);
        var plannedEndBefore = Dt(flagged, "plannedEnd");

        // Scheduler run while creep is unapproved: CX's plan is frozen, and CY queues behind the frozen plan.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);
        var yBefore = Dt(await ItemFor(admin, "CY"), "plannedStart");
        Assert.True(yBefore >= plannedEndBefore.AddSeconds(-1), "CY must queue behind CX's frozen plan");
        Assert.Equal(plannedEndBefore, Dt(await ItemFor(admin, "CX"), "plannedEnd"));

        // Admin approves the creep.
        var itemId = (await ItemFor(admin, "CX")).GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/schedule-items/{itemId}/approve-creep", null)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        // Alert is resolved (approved), history kept, and the queue now reflects 75 EqA (450 min) still to do.
        var approved = await ItemFor(admin, "CX");
        Assert.True(approved.GetProperty("creepDetected").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, approved.GetProperty("creepApprovedAt").ValueKind);
        Assert.Equal(plannedEndBefore, Dt(approved, "plannedEnd")); // the original plan stays as history

        var yAfter = Dt(await ItemFor(admin, "CY"), "plannedStart");
        Assert.True(yAfter - yBefore > TimeSpan.FromHours(3), $"CY should slide ~3.5h once CX's real completion is projected (was {yBefore:O}, now {yAfter:O})");

        var job = await _f.WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.JobRef == "CX"));
        Assert.True(job.EstimatedCompletionDate > plannedEndBefore.AddHours(3), "CX's projected completion should reflect the slower real rate");
    }
}
