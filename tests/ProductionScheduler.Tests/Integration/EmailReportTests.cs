using ProductionScheduler.Domain;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Scheduling.BackgroundServices;
using ProductionScheduler.Scheduling.Email;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// §5.17 email reports: what each contains, that each goes once per working day, and that the mail
// settings stay private.
[Collection(IntegrationCollection.Name)]
public class EmailReportTests
{
    private readonly IntegrationFixture _f;

    public EmailReportTests(IntegrationFixture fixture) => _f = fixture;

    private static Job NewJob(string jobRef, string design, int dueInDays) => new()
    {
        JobRef = jobRef, ComponentType = "MailProduct", DesignStatus = design, Status = design == "Quote" ? "scheduled" : "ready", TotalEqA = 12,
        ClientName = "Client " + jobRef, EstBookedDeliveryDate = DateOnly.FromDateTime(SiteClock.Now).AddDays(dueInDays),
        CreatedAt = SiteClock.Now, UpdatedAt = SiteClock.Now,
    };

    private Task<EmailReport> BuildAsync(string key) => _f.WithDbAsync(db => EmailReports.BuildAsync(db, key, SiteClock.Now));

    private async Task<string> UsePickupFolderAsync()
    {
        var pickup = _f.NewDir("mail-pickup");
        await _f.SetSettingsAsync(("EmailPickupDirectory", pickup), ("SmtpFromAddress", "scheduler@example.com"));
        await _f.WithDbAsync(db => db.MaintenanceLogs.Where(l => l.TaskName.StartsWith("Email")).ExecuteDeleteAsync());
        return pickup;
    }

    [Fact]
    public async Task NextDayJobs_ListsWhatIsPlannedForTheNextWorkingDay()
    {
        var stationId = await _f.AddStationAsync("Mail Line", "MailProduct");
        var tomorrow = DateOnly.FromDateTime(SiteClock.Now).AddDays(1); // the test calendar works every day
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.Add(NewJob("MAIL-NEXT", "FinalDesign", 9));
            var start = tomorrow.ToDateTime(new TimeOnly(9, 0));
            db.ScheduleItems.Add(new ScheduleItem
            {
                JobRef = "MAIL-NEXT", StationId = stationId, ScheduleDate = tomorrow, PlannedStart = start, PlannedEnd = start.AddHours(2),
                PlannedEqA = 12, GeneratedAt = SiteClock.Now,
                Segments = [new ScheduleSegment { SegmentDate = tomorrow, SegmentStart = start, SegmentEnd = start.AddHours(2), PlannedEqA = 12 }],
            });
            await db.SaveChangesAsync();
        });

        var report = await BuildAsync(EmailReports.NextDayJobs);

        Assert.Contains("MAIL-NEXT", report.HtmlBody);
        Assert.Contains("Mail Line", report.HtmlBody);
        Assert.Contains("09:00", report.HtmlBody);
    }

    [Fact]
    public async Task ReadyForProduction_ListsQuoteJobsDueWithinTheWindow_Only()
    {
        await _f.SetSettingsAsync(("EmailReadyForProductionDays", "7"));
        await _f.WithDbAsync(async db =>
        {
            db.Jobs.AddRange(NewJob("MAIL-Q-SOON", "Quote", 3), NewJob("MAIL-Q-LATER", "Quote", 30), NewJob("MAIL-F-SOON", "FinalDesign", 3));
            await db.SaveChangesAsync();
        });

        var report = await BuildAsync(EmailReports.ReadyForProduction);

        Assert.Contains("MAIL-Q-SOON", report.HtmlBody);
        Assert.DoesNotContain("MAIL-Q-LATER", report.HtmlBody);
        Assert.DoesNotContain("MAIL-F-SOON", report.HtmlBody);
    }

    [Fact]
    public async Task DailyProduction_TotalsWhatWasMadeToday()
    {
        var stationId = await _f.AddStationAsync("Mail Made Line", "MailMade", dailyEqATarget: 40);
        await _f.WithDbAsync(async db =>
        {
            var job = NewJob("MAIL-MADE", "FinalDesign", 5);
            job.ComponentType = "MailMade";
            job.AssignedStationId = stationId;
            job.Members = [new JobMember { JobRef = "MAIL-MADE", MemberId = "E01", EqaValue = 20, Done = true, CompletedAt = SiteClock.Now, CompletedEqA = 20 }];
            db.Jobs.Add(job);
            await db.SaveChangesAsync();
        });

        var report = await BuildAsync(EmailReports.DailyProduction);

        Assert.Contains("Mail Made Line", report.HtmlBody);
        Assert.Contains("50%", report.HtmlBody); // 20 of a 40 Eq target
    }

    [Fact]
    public async Task EachReport_GoesOncePerDay_AndOnlyWhenItHasARecipient()
    {
        var pickup = await UsePickupFolderAsync();
        await _f.SetSettingsAsync(
            ("EmailNextDayJobsTo", "production@example.com"), ("EmailNextDayJobsHour", "0"),
            ("EmailReadyForProductionTo", ""), ("EmailDailyProductionTo", ""));
        var service = new EmailReportService(_f.ScopeFactory, NullLogger<EmailReportService>.Instance);

        await service.PollAsync(default);
        await service.PollAsync(default);

        var sent = Directory.GetFiles(pickup, "*.eml");
        var message = await File.ReadAllTextAsync(Assert.Single(sent));
        Assert.Contains("production@example.com", message);
        Assert.Equal(1, await _f.WithDbAsync(db => db.MaintenanceLogs.CountAsync(l => l.TaskName == "EmailNextDayJobs" && l.Outcome == "success")));
        Assert.False(await _f.WithDbAsync(db => db.MaintenanceLogs.AnyAsync(l => l.TaskName == "EmailDailyProduction")));
        await _f.SetSettingsAsync(("EmailNextDayJobsTo", ""));
    }

    [Fact]
    public async Task SendNow_ReportsWhyItCouldNotSend()
    {
        await UsePickupFolderAsync();
        await _f.SetSettingsAsync(("EmailPickupDirectory", ""), ("SmtpHost", ""), ("EmailDailyProductionTo", "office@example.com"));
        var admin = await _f.ClientAsync();

        var result = await (await admin.PostAsync("/api/email/reports/DailyProduction/send-now", null)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(result.GetProperty("sent").GetBoolean());
        Assert.Contains("No SMTP server", result.GetProperty("message").GetString());
        var status = (await admin.GetFromJsonAsync<JsonElement>("/api/email/reports")).EnumerateArray().Single(r => r.GetProperty("key").GetString() == "DailyProduction");
        Assert.Contains("No SMTP server", status.GetProperty("lastError").GetString());
        await _f.SetSettingsAsync(("EmailDailyProductionTo", ""));
    }

    [Fact]
    public async Task MailSettings_AreAdminOnly_AndThePasswordIsNeverSentBack()
    {
        await _f.SetSettingsAsync(("SmtpPassword", "s3cret"));
        var admin = await _f.ClientAsync();
        var created = await admin.PostAsJsonAsync("/api/users", new { username = "mail_viewer", password = "Pass1234!", role = "Viewer" });
        Assert.True(created.IsSuccessStatusCode || created.StatusCode == HttpStatusCode.Conflict);
        var viewer = await _f.ClientAsync("mail_viewer", "Pass1234!");

        var viewerKeys = (await viewer.GetFromJsonAsync<JsonElement>("/api/settings")).EnumerateArray().Select(s => s.GetProperty("key").GetString()!).ToList();
        Assert.NotEmpty(viewerKeys);
        Assert.All(viewerKeys, k => Assert.StartsWith("Theme", k));

        var password = (await admin.GetFromJsonAsync<JsonElement>("/api/settings")).EnumerateArray().Single(s => s.GetProperty("key").GetString() == "SmtpPassword");
        Assert.Equal("********", password.GetProperty("value").GetString());

        // Saving the form with the mask untouched keeps the real password.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/settings/SmtpPassword", new { value = "********" })).StatusCode);
        Assert.Equal("s3cret", await _f.WithDbAsync(db => db.Settings.Where(s => s.Key == "SmtpPassword").Select(s => s.Value).SingleAsync()));
    }

    [Theory]
    [InlineData("EmailDailyProductionTo", "not-an-address")]
    [InlineData("EmailNextDayJobsTo", "a@example.com; nope")]
    [InlineData("SmtpUseSsl", "yes")]
    public async Task BadMailSettings_AreRefused(string key, string value)
    {
        var admin = await _f.ClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/settings/{key}", new { value })).StatusCode);
    }
}
