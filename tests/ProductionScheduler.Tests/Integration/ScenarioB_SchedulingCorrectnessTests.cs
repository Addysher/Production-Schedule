using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Scheduling.Engine;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §7 Step 8 — Scenario B: 10+ jobs across 3 stations, mixed due dates / EqA. EDD beats worse
// orderings, ATCS beats EDD when setups are significant, pinned jobs never move — plus the
// robustness cases that fall out of running a real, messy queue through the scheduler.
[Collection(IntegrationCollection.Name)]
public class ScenarioB_SchedulingCorrectnessTests
{
    private readonly IntegrationFixture _f;

    public ScenarioB_SchedulingCorrectnessTests(IntegrationFixture fixture) => _f = fixture;

    private static Job ReadyJob(string jobRef, string comp, decimal eqa, DateOnly due) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        ExpectedDeliveryDate = due, EstBookedDeliveryDate = due, CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private async Task<List<JsonElement>> ScheduleFor(HttpClient admin, string jobRefPrefix)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Where(i => i.GetProperty("jobRef").GetString()!.StartsWith(jobRefPrefix)).ToList();
    }

    private static DateTime Dt(JsonElement e, string name) => e.GetProperty(name).GetDateTime();

    [Fact]
    public async Task Edd_TwelveJobsAcrossThreeStations_HasNoOverlapsAndBeatsWorseOrderings()
    {
        const string comp = "ScnB";
        for (var s = 1; s <= 3; s++) await _f.AddStationAsync($"ScnB Station {s}", comp);

        var today = DateOnly.FromDateTime(SiteClock.Now);
        var eqa = new[] { 480, 240, 720, 360, 600, 300, 840, 420, 540, 660, 270, 780 };
        var dueDays = new[] { 1, 0, 2, 1, 3, 0, 2, 1, 3, 2, 0, 3 };
        await _f.WithDbAsync(async db =>
        {
            for (var i = 0; i < 12; i++) db.Jobs.Add(ReadyJob($"BEDD{i:D2}", comp, eqa[i], today.AddDays(dueDays[i])));
            await db.SaveChangesAsync();
        });

        var admin = await _f.ClientAsync();
        var generate = await admin.PostAsync("/api/schedule/generate", null);
        Assert.Equal(HttpStatusCode.OK, generate.StatusCode);

        var items = await ScheduleFor(admin, "BEDD");
        Assert.Equal(12, items.Count);

        var due = Enumerable.Range(0, 12).ToDictionary(i => $"BEDD{i:D2}", i => today.AddDays(dueDays[i]).ToDateTime(TimeOnly.MinValue));
        var minutes = Enumerable.Range(0, 12).ToDictionary(i => $"BEDD{i:D2}", i => (double)eqa[i]);

        decimal actualTotal = 0, shuffledTotal = 0, reverseTotal = 0;
        double actualMax = 0, shuffledMax = 0, reverseMax = 0;
        var rng = new Random(7);

        foreach (var station in items.GroupBy(i => i.GetProperty("stationId").GetInt32()))
        {
            var ordered = station.OrderBy(i => Dt(i, "plannedStart")).ToList();

            // No station runs two jobs at once.
            for (var k = 1; k < ordered.Count; k++)
                Assert.True(Dt(ordered[k], "plannedStart") >= Dt(ordered[k - 1], "plannedEnd").AddSeconds(-1));

            var start = Dt(ordered[0], "plannedStart");
            var refs = ordered.Select(i => i.GetProperty("jobRef").GetString()!).ToList();

            var actual = ordered.Select(i => new TardinessJob(i.GetProperty("jobRef").GetString()!, due[i.GetProperty("jobRef").GetString()!], Dt(i, "plannedEnd"))).ToList();
            actualTotal += TardinessCalc.TotalWeightedTardinessMinutes(actual);
            actualMax = Math.Max(actualMax, TardinessCalc.MaxTardinessMinutes(actual));

            List<TardinessJob> Simulate(IEnumerable<string> order)
            {
                var t = start;
                return order.Select(r => { t = t.AddMinutes(minutes[r]); return new TardinessJob(r, due[r], t); }).ToList();
            }

            var reverse = Simulate(refs.OrderByDescending(r => due[r]).ThenByDescending(r => r));
            var shuffled = Simulate(refs.OrderBy(_ => rng.Next()));
            reverseTotal += TardinessCalc.TotalWeightedTardinessMinutes(reverse);
            reverseMax = Math.Max(reverseMax, TardinessCalc.MaxTardinessMinutes(reverse));
            shuffledTotal += TardinessCalc.TotalWeightedTardinessMinutes(shuffled);
            shuffledMax = Math.Max(shuffledMax, TardinessCalc.MaxTardinessMinutes(shuffled));
        }

        // EDD provably minimises the worst single tardiness; it must never lose on that measure...
        var dump = string.Join(" | ", items.GroupBy(i => i.GetProperty("stationId").GetInt32()).Select(g => $"st{g.Key}: " + string.Join(",", g.OrderBy(i => Dt(i, "plannedStart")).Select(i => $"{i.GetProperty("jobRef").GetString()!["BEDD".Length..]}(due{due[i.GetProperty("jobRef").GetString()!]:MMdd},want{minutes[i.GetProperty("jobRef").GetString()!]},got{(Dt(i, "plannedEnd") - Dt(i, "plannedStart")).TotalMinutes:F0}@{Dt(i, "plannedStart"):dd HH:mm})"))));
        Assert.True(actualMax <= shuffledMax + 0.001, $"EDD max tardiness {actualMax} vs shuffled {shuffledMax}. Order per station: {dump}");
        Assert.True(actualMax <= reverseMax + 0.001, $"EDD max tardiness {actualMax} vs reverse {reverseMax}");
        // ...and on this data set the total is strictly better than the deliberately worst ordering.
        Assert.True(actualTotal < reverseTotal, $"EDD total tardiness {actualTotal} vs reverse-EDD {reverseTotal}");
    }

    [Fact]
    public void Atcs_ReducesTardinessVersusEdd_WhenSetupTimesAreSignificant()
    {
        // Three component types interleaved by due date; a 45-minute changeover between any two
        // different types. Strict EDD ping-pongs between types; ATCS batches them.
        var t0 = new DateTime(2026, 9, 21, 6, 0, 0);
        var types = new[] { "A", "B", "C" };
        var jobs = Enumerable.Range(0, 12)
            .Select(i => new SequencerJob($"J{i:D2}", types[i % 3], t0.AddMinutes(330 + i * 5), 30))
            .ToList();
        int Setup(string from, string to) => from == to ? 0 : 45;

        decimal Score(IReadOnlyList<string> order)
        {
            var byRef = jobs.ToDictionary(j => j.JobRef);
            var t = t0;
            string? prev = null;
            var scored = new List<TardinessJob>();
            foreach (var r in order)
            {
                var j = byRef[r];
                t = t.AddMinutes((prev is null ? 0 : Setup(prev, j.ComponentType)) + (double)j.ProcessingMinutes);
                scored.Add(new TardinessJob(r, j.DueDate, t));
                prev = j.ComponentType;
            }
            return TardinessCalc.TotalWeightedTardinessMinutes(scored);
        }

        var edd = Score(Sequencer.SequenceEdd(jobs));
        var atcs = Score(Sequencer.SequenceAtcs(jobs, Setup, t0));

        Assert.True(atcs < edd, $"ATCS total tardiness {atcs} should beat EDD {edd} when setups dominate");
    }

    [Fact]
    public async Task PinnedJob_IsNeverMovedByLaterSchedulerRuns()
    {
        const string comp = "ScnBPin";
        var stationA = await _f.AddStationAsync("ScnBPin A", comp);
        await _f.AddStationAsync("ScnBPin B", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        var pinDate = today.AddDays(5);

        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(ReadyJob("BPIN-PINNED", comp, 120, today.AddDays(10)));
            db.Jobs.Add(ReadyJob("BPIN-1", comp, 300, today.AddDays(1)));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var pin = await admin.PostAsJsonAsync("/api/jobs/BPIN-PINNED/schedule-manually", new { stationId = stationA, scheduleDate = pinDate });
        Assert.Equal(HttpStatusCode.OK, pin.StatusCode);

        // More work arrives and the scheduler runs twice more.
        await _f.WithDbAsync(async db =>
        {
            for (var i = 2; i <= 5; i++) db.Jobs.Add(ReadyJob($"BPIN-{i}", comp, 200 + i * 20, today.AddDays(i)));
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        var pinned = (await ScheduleFor(admin, "BPIN-PINNED")).Single();
        Assert.Equal(stationA, pinned.GetProperty("stationId").GetInt32());
        Assert.Equal(pinDate, DateOnly.FromDateTime(Dt(pinned, "plannedStart")));

        // Unpinning hands it back to the automatic scheduler.
        var unpin = await admin.DeleteAsync("/api/jobs/BPIN-PINNED/schedule-manually");
        Assert.Equal(HttpStatusCode.OK, unpin.StatusCode);
        var afterUnpin = (await ScheduleFor(admin, "BPIN-PINNED")).Single();
        Assert.NotEqual(pinDate, DateOnly.FromDateTime(Dt(afterUnpin, "plannedStart")));
    }

    [Fact]
    public async Task PinningToAStationOfADifferentComponentType_IsRejected()
    {
        var wrongStation = await _f.AddStationAsync("ScnBWrong Trusses", "ScnBWrongType");
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.AddStationAsync("ScnBWrong Walls", "ScnBWrongWalls");
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("BWRONG-1", "ScnBWrongWalls", 100, today.AddDays(3))); await db.SaveChangesAsync(); });

        var admin = await _f.ClientAsync();
        var response = await admin.PostAsJsonAsync("/api/jobs/BWRONG-1/schedule-manually", new { stationId = wrongStation, scheduleDate = today.AddDays(2) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // §4.4: station ComponentType must match the job's
    }

    [Fact]
    public async Task JobWithNoCompatibleStation_DoesNotBlockTheRestOfTheSchedule()
    {
        const string comp = "ScnBOk";
        await _f.AddStationAsync("ScnBOk Station", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(ReadyJob("BORPHAN-1", "ScnBNoStationAnywhere", 100, today.AddDays(2)));
            db.Jobs.Add(ReadyJob("BOK-1", comp, 100, today.AddDays(2)));
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        try
        {
            var response = await admin.PostAsync("/api/schedule/generate", null);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Single(await ScheduleFor(admin, "BOK-1"));
            Assert.Empty(await ScheduleFor(admin, "BORPHAN-1"));
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(body.GetProperty("jobsSkipped").GetInt32() >= 1);
        }
        finally
        {
            // Leave no permanently-unroutable job behind for later scenarios.
            await _f.WithDbAsync(db => db.Jobs.Where(j => j.JobRef == "BORPHAN-1").ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task Precedence_IsHonouredWhileUnconstrainedJobsStillFollowEdd()
    {
        const string comp = "ScnBPrec";
        await _f.AddStationAsync("ScnBPrec Station", comp);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(ReadyJob("BPREC-P1", comp, 200, today.AddDays(9))); // must run first...
            db.Jobs.Add(ReadyJob("BPREC-P2", comp, 200, today.AddDays(1))); // ...even though this is due sooner
            db.Jobs.Add(ReadyJob("BPREC-Q", comp, 200, today.AddDays(2)));  // free job
            await db.SaveChangesAsync();
            db.Precedences.Add(new Precedence { PredecessorJobRef = "BPREC-P1", SuccessorJobRef = "BPREC-P2" });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        var byRef = (await ScheduleFor(admin, "BPREC-")).ToDictionary(i => i.GetProperty("jobRef").GetString()!);
        Assert.True(Dt(byRef["BPREC-P2"], "plannedStart") >= Dt(byRef["BPREC-P1"], "plannedEnd").AddSeconds(-1), "successor must not start before its predecessor ends");
        // Among the jobs free to run first (P1 and Q), the earlier due date (Q) goes first.
        Assert.True(Dt(byRef["BPREC-Q"], "plannedStart") < Dt(byRef["BPREC-P1"], "plannedStart"), "EDD should still order the unconstrained jobs");
    }
}
