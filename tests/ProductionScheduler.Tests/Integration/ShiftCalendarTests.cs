using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// The Shifts tab shapes the working day: the active shifts' start/end set its hours (to the minute), their
// breaks are never worked through, saving a shift re-plans straight away, and frozen and running work
// follow the new day too.
[Collection(IntegrationCollection.Name)]
public class ShiftCalendarTests
{
    private readonly IntegrationFixture _f;

    public ShiftCalendarTests(IntegrationFixture fixture) => _f = fixture;

    private static Job ReadyJob(string jobRef, string comp, decimal eqa) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        EstBookedDeliveryDate = SiteClock.Today.AddDays(60), CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<List<(DateTime Start, DateTime End)>> SegmentsAsync(HttpClient admin, string jobRef)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Where(i => i.GetProperty("jobRef").GetString() == jobRef)
            .SelectMany(i => i.GetProperty("segments").EnumerateArray())
            .Select(s => (s.GetProperty("start").GetDateTime(), s.GetProperty("end").GetDateTime()))
            .ToList();
    }

    private static object Shift(string name, string start, string end, params (string Start, int Mins)[] breaks) => new
    {
        name, startTime = start, endTime = end, isActive = true,
        breaks = breaks.Select(b => new { breakStart = b.Start, breakDurationMins = b.Mins }).ToArray(),
    };

    private static void AssertNoneOverlap(IEnumerable<(DateTime Start, DateTime End)> segments, TimeSpan from, TimeSpan to)
    {
        foreach (var (start, end) in segments)
        {
            var windowStart = start.Date + from;
            Assert.False(start < start.Date + to && end > windowStart, $"segment {start:O}-{end:O} runs through {from}-{to}");
        }
    }

    [Fact]
    public async Task TheShiftsHours_SetTheWorkingDay_ToTheMinute_AndSavingRePlans()
    {
        await _f.AddStationAsync("Shift Hours Line", "ShiftHours");
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("SHIFT-HRS", "ShiftHours", 1200)); await db.SaveChangesAsync(); }); // 20 h of work
        var admin = await _f.ClientAsync();

        // No Recalculate: saving the shift re-plans.
        var created = await admin.PostAsJsonAsync("/api/shifts", Shift("Hours test", "06:00:00", "14:30:00", ("10:00:00", 30)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        try
        {
            var segments = await SegmentsAsync(admin, "SHIFT-HRS");
            Assert.NotEmpty(segments);
            Assert.All(segments, s =>
            {
                Assert.True(s.Start.TimeOfDay >= TimeSpan.FromHours(6), $"{s.Start:O} starts before 06:00");
                Assert.True(s.End.TimeOfDay <= new TimeSpan(14, 30, 0) && s.End.Date == s.Start.Date, $"{s.End:O} runs past 14:30");
            });
            AssertNoneOverlap(segments, TimeSpan.FromHours(10), new TimeSpan(10, 30, 0));

            var calendar = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/calendar-settings");
            Assert.Equal("06:00", calendar.GetProperty("dayStart").GetString());
            Assert.Equal("14:30", calendar.GetProperty("dayEnd").GetString());
            Assert.Equal(15, calendar.GetProperty("shiftEndHour").GetInt32()); // the grid shows the 14:00 hour in full
        }
        finally
        {
            await admin.DeleteAsync($"/api/shifts/{shiftId}");
        }
    }

    [Theory]
    [InlineData("06:00:00", "14:30:00", "21:00:00", 15, "outside the shift")] // the reported case: a break after the shift
    [InlineData("06:00:00", "14:30:00", "14:15:00", 30, "outside the shift")] // runs past the end
    [InlineData("14:00:00", "06:00:00", "08:00:00", 15, "must end after it starts")]
    public async Task ABreakOutsideTheShift_OrAShiftEndingBeforeItStarts_IsRefused(string start, string end, string breakStart, int mins, string reason)
    {
        var admin = await _f.ClientAsync();

        var response = await admin.PostAsJsonAsync("/api/shifts", Shift("Bad shift", start, end, (breakStart, mins)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task FrozenWork_KeepsItsLineAndStart_ButFollowsANewBreak()
    {
        var station = await _f.AddStationAsync("Shift Frozen Line", "ShiftFrozen");
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("SHIFT-FRZ", "ShiftFrozen", 1800)); await db.SaveChangesAsync(); }); // 30 h: spans every time of day
        var admin = await _f.ClientAsync();
        await admin.PostAsync("/api/schedule/generate", null);
        var before = await SegmentsAsync(admin, "SHIFT-FRZ");
        await _f.SetSettingsAsync(("FreezeWindowDays", "3"));
        int? shiftId = null;
        try
        {
            var created = await admin.PostAsJsonAsync("/api/shifts", Shift("Frozen lunch", "00:00:00", "00:00:00", ("12:00:00", 60)));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

            var after = await SegmentsAsync(admin, "SHIFT-FRZ");
            AssertNoneOverlap(after, TimeSpan.FromHours(12), TimeSpan.FromHours(13));
            Assert.True(after.Min(s => s.Start) >= before.Min(s => s.Start));
            Assert.True(after.Min(s => s.Start) - before.Min(s => s.Start) <= TimeSpan.FromHours(1)); // at most pushed out of a break
            Assert.Equal(station, await _f.WithDbAsync(db => db.Jobs.Where(j => j.JobRef == "SHIFT-FRZ").Select(j => j.AssignedStationId).SingleAsync()));
        }
        finally
        {
            if (shiftId is int id) await admin.DeleteAsync($"/api/shifts/{id}");
            await _f.SetSettingsAsync(("FreezeWindowDays", "0"));
        }
    }

    [Fact]
    public async Task RunningWork_IsRedrawnAroundANewBreak()
    {
        var station = await _f.AddStationAsync("Shift Running Line", "ShiftRunning");
        await _f.WithDbAsync(async db =>
        {
            var job = ReadyJob("SHIFT-RUN", "ShiftRunning", 1800);
            job.Status = "in-production";
            job.AssignedStationId = station;
            var now = SiteClock.Now;
            job.ScheduleItems =
            [
                new ScheduleItem
                {
                    JobRef = "SHIFT-RUN", StationId = station, ScheduleDate = DateOnly.FromDateTime(now), PlannedStart = now.AddHours(-1),
                    PlannedEnd = now.AddHours(29), PlannedEqA = 1800, ActualStart = now.AddHours(-1), GeneratedAt = now,
                    Segments = [new ScheduleSegment { SegmentDate = DateOnly.FromDateTime(now), SegmentStart = now.AddHours(-1), SegmentEnd = now.AddHours(29), PlannedEqA = 1800 }],
                },
            ];
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();
        var created = await admin.PostAsJsonAsync("/api/shifts", Shift("Running lunch", "00:00:00", "00:00:00", ("12:00:00", 60)));
        var shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        try
        {
            var segments = await SegmentsAsync(admin, "SHIFT-RUN");
            var future = segments.Where(s => s.Start >= SiteClock.Now.AddMinutes(-1)).ToList();
            Assert.NotEmpty(future);
            AssertNoneOverlap(future, TimeSpan.FromHours(12), TimeSpan.FromHours(13));
            Assert.Contains(segments, s => s.Start <= SiteClock.Now.AddMinutes(-59)); // what already happened is kept
        }
        finally
        {
            await admin.DeleteAsync($"/api/shifts/{shiftId}");
        }
    }
}
