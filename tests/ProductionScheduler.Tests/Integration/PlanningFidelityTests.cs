using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Domain;
using ProductionScheduler.Domain.Entities;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Gap-closing checks: multi-day jobs are planned per day, shift breaks are honoured, the reports
// credit EqA to the day it was really produced, Viewers can read everything their pages need, and the
// notification badge counts what happened since the caller last looked.
[Collection(IntegrationCollection.Name)]
public class PlanningFidelityTests
{
    private readonly IntegrationFixture _f;

    public PlanningFidelityTests(IntegrationFixture fixture) => _f = fixture;

    private static Job ReadyJob(string jobRef, string comp, decimal eqa) => new()
    {
        JobRef = jobRef, ComponentType = comp, DesignStatus = "FinalDesign", Status = "ready", TotalEqA = eqa,
        EstBookedDeliveryDate = SiteClock.Today.AddDays(60), ExpectedDeliveryDate = SiteClock.Today.AddDays(60),
        CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private static async Task<JsonElement> ScheduleItemAsync(HttpClient admin, string jobRef)
    {
        var all = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/current");
        return all.EnumerateArray().Single(i => i.GetProperty("jobRef").GetString() == jobRef);
    }

    [Fact]
    public async Task MultiDayJob_IsPlannedPerDay_WithSegmentsThatAddUpToTheWholeJob()
    {
        await _f.AddStationAsync("PF Multi Station", "PfMulti", eqaPerHour: 4); // 96 Eq a (24 h) day
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("PFM1", "PfMulti", 250)); await db.SaveChangesAsync(); });
        var admin = await _f.ClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);

        var item = await ScheduleItemAsync(admin, "PFM1");
        var segments = item.GetProperty("segments").EnumerateArray().ToList();

        Assert.True(segments.Count >= 3, "250 EqA at 96/day needs at least three days");
        Assert.Equal(250m, segments.Sum(s => s.GetProperty("plannedEqA").GetDecimal()));
        Assert.All(segments, s => Assert.True(s.GetProperty("plannedEqA").GetDecimal() <= 100m));

        // Each day's segments lie inside that day, and the last one ends where the job ends.
        foreach (var s in segments)
            Assert.Equal(DateOnly.Parse(s.GetProperty("date").GetString()!), DateOnly.FromDateTime(s.GetProperty("start").GetDateTime()));
        Assert.Equal(item.GetProperty("plannedEnd").GetDateTime(), segments.Max(s => s.GetProperty("end").GetDateTime()));
        Assert.Equal(item.GetProperty("plannedStart").GetDateTime(), segments.Min(s => s.GetProperty("start").GetDateTime()));
    }

    [Fact]
    public async Task ShiftBreaks_AreNeverWorkedThrough_AndStretchTheJob()
    {
        await _f.AddStationAsync("PF Break Station", "PfBreak");
        await _f.WithDbAsync(async db => { db.Jobs.Add(ReadyJob("PFB1", "PfBreak", 1800)); await db.SaveChangesAsync(); }); // 30h: always spans a noon
        var admin = await _f.ClientAsync();

        var created = await admin.PostAsJsonAsync("/api/shifts", new
        {
            name = "PF lunch", startTime = "00:00:00", endTime = "00:00:00" /* midnight to midnight */, isActive = true,
            breaks = new[] { new { breakStart = "12:00:00", breakDurationMins = 60 } },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        try
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/schedule/generate", null)).StatusCode);
            var item = await ScheduleItemAsync(admin, "PFB1");
            var segments = item.GetProperty("segments").EnumerateArray().ToList();

            // No stretch of work overlaps 12:00-13:00 on any day.
            foreach (var s in segments)
            {
                var start = s.GetProperty("start").GetDateTime();
                var end = s.GetProperty("end").GetDateTime();
                var breakStart = start.Date.AddHours(12);
                Assert.False(start < breakStart.AddHours(1) && end > breakStart, $"segment {start:O}-{end:O} runs through the lunch break");
            }

            // The job takes its 30 hours of work plus one hour for every lunch break it had to wait through.
            var plannedStart = item.GetProperty("plannedStart").GetDateTime();
            var plannedEnd = item.GetProperty("plannedEnd").GetDateTime();
            var breaksWaited = Enumerable.Range(0, 4).Select(d => plannedStart.Date.AddDays(d).AddHours(12))
                .Count(noon => noon >= plannedStart && noon.AddHours(1) <= plannedEnd);
            Assert.True(breaksWaited >= 1);
            Assert.Equal(1800 + 60 * breaksWaited, (plannedEnd - plannedStart).TotalMinutes, 3);
        }
        finally
        {
            await admin.DeleteAsync($"/api/shifts/{shiftId}"); // deactivates the shift, so its break stops applying
        }
    }

    [Fact]
    public async Task ProductionReport_CreditsEqaToTheDayItWasCompleted()
    {
        var stationId = await _f.AddStationAsync("PF Report Station", "PfReport");
        var day1 = new DateTime(2035, 4, 10, 15, 0, 0);
        var day2 = new DateTime(2035, 4, 11, 9, 0, 0);
        await _f.WithDbAsync(async db =>
        {
            var job = ReadyJob("PFR1", "PfReport", 90);
            job.Status = "completed";
            job.AssignedStationId = stationId;
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
            db.JobMembers.AddRange(
                new JobMember { JobRef = "PFR1", MemberId = "M1", Batch = 1, Sequence = 1, SubSequence = 1, EqaValue = 60, Done = true, CompletedAt = day1, CompletedEqA = 60 },
                new JobMember { JobRef = "PFR1", MemberId = "M2", Batch = 1, Sequence = 1, SubSequence = 2, EqaValue = 30, Done = true, CompletedAt = day2, CompletedEqA = 30 });
            await db.SaveChangesAsync();
        });
        var admin = await _f.ClientAsync();

        var rows = await admin.GetFromJsonAsync<JsonElement>("/api/reports/production?dateFrom=2035-04-10&dateTo=2035-04-11");
        var mine = rows.EnumerateArray().Where(r => r.GetProperty("stationId").GetInt32() == stationId).ToList();

        Assert.Equal(2, mine.Count);
        Assert.Equal(60m, mine.Single(r => r.GetProperty("date").GetString() == "2035-04-10").GetProperty("eqa").GetDecimal());
        Assert.Equal(30m, mine.Single(r => r.GetProperty("date").GetString() == "2035-04-11").GetProperty("eqa").GetDecimal());

        var narrower = await admin.GetFromJsonAsync<JsonElement>("/api/reports/production?dateFrom=2035-04-11&dateTo=2035-04-11");
        Assert.DoesNotContain(narrower.EnumerateArray(), r => r.GetProperty("stationId").GetInt32() == stationId && r.GetProperty("date").GetString() == "2035-04-10");
    }

    [Fact]
    public async Task Viewer_CanReadEverythingTheViewerPagesNeed()
    {
        var admin = await _f.ClientAsync();
        await admin.PostAsJsonAsync("/api/users", new { username = "viewer_pf", password = "Viewer123!", role = "Viewer" });
        var viewer = await _f.ClientAsync("viewer_pf", "Viewer123!");

        // The exact reads made by the Dashboard, Calendar, Jobs, Quotes and Reports pages.
        foreach (var path in new[]
                 {
                     "/api/jobs", "/api/schedule/current", "/api/schedule/calendar-settings", "/api/stations",
                     "/api/stations/1/capacity-overrides", "/api/maintenance-logs?take=50", "/api/jobtypes",
                     "/api/reports/production?dateFrom=2030-01-01&dateTo=2030-01-02", "/api/notifications",
                 })
        {
            var response = await viewer.GetAsync(path);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"Viewer GET {path} -> {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task CalendarSettings_ReportsTheWindowWorkingDaysAndBreaks()
    {
        var admin = await _f.ClientAsync();
        var created = await admin.PostAsJsonAsync("/api/shifts", new
        {
            name = "PF calendar", startTime = "06:00:00", endTime = "18:00:00", isActive = true,
            breaks = new[] { new { breakStart = "09:30:00", breakDurationMins = 15 } },
        });
        var shiftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        try
        {
            var settings = await admin.GetFromJsonAsync<JsonElement>("/api/schedule/calendar-settings");

            // The active shift sets the working day (the fixture's own 24h Settings hours only apply with no shift).
            Assert.Equal(6, settings.GetProperty("shiftStartHour").GetInt32());
            Assert.Equal(18, settings.GetProperty("shiftEndHour").GetInt32());
            Assert.Equal("06:00", settings.GetProperty("dayStart").GetString());
            Assert.Equal(7, settings.GetProperty("workingDays").GetArrayLength());
            Assert.Contains(settings.GetProperty("breaks").EnumerateArray(),
                b => b.GetProperty("start").GetString() == "09:30" && b.GetProperty("end").GetString() == "09:45");
        }
        finally
        {
            await admin.DeleteAsync($"/api/shifts/{shiftId}");
        }
    }

    [Fact]
    public async Task Notifications_CountNewJobsAndErrorsSinceTheCallerLastLooked()
    {
        var admin = await _f.ClientAsync();

        var first = await admin.GetFromJsonAsync<JsonElement>("/api/notifications");
        Assert.Equal(0, first.GetProperty("newJobs").GetInt32());
        Assert.Equal(0, first.GetProperty("errors").GetInt32());
        var marker = first.GetProperty("serverNow").GetDateTime();

        await Task.Delay(50);
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(ReadyJob("PFN1", "PfNotify", 10));
            db.Jobs.Add(ReadyJob("PFN2", "PfNotify", 10));
            db.MaintenanceLogs.Add(new MaintenanceLog { TaskName = "NotifyTest", Outcome = "error", Message = "bad file", RunAt = SiteClock.Now });
            db.MaintenanceLogs.Add(new MaintenanceLog { TaskName = "NotifyTest", Outcome = "success", Message = "fine", RunAt = SiteClock.Now });
            await db.SaveChangesAsync();
        });

        var since = await admin.GetFromJsonAsync<JsonElement>($"/api/notifications?since={Uri.EscapeDataString(marker.ToString("O"))}");
        Assert.Equal(2, since.GetProperty("newJobs").GetInt32());
        Assert.Equal(1, since.GetProperty("errors").GetInt32()); // successes are not errors

        // Looking again from the new marker: nothing is new.
        var again = await admin.GetFromJsonAsync<JsonElement>($"/api/notifications?since={Uri.EscapeDataString(since.GetProperty("serverNow").GetDateTime().ToString("O"))}");
        Assert.Equal(0, again.GetProperty("newJobs").GetInt32());
        Assert.Equal(0, again.GetProperty("errors").GetInt32());
    }
}
