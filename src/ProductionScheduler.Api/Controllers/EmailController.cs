using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.BackgroundServices;
using ProductionScheduler.Scheduling.Email;

namespace ProductionScheduler.Api.Controllers;

public record EmailReportStatusDto(string Key, string Title, string Recipients, int Hour, DateTime? LastSentAt, string? LastError, DateTime? LastErrorAt);

public record SendEmailResultDto(bool Sent, string Message);

// Settings → Email (§5.21): what each report is set to, a preview of what it would contain now, and a
// Send now button. The schedule itself runs in EmailReportService.
[ApiController]
[Route("api/email")]
[Authorize(Roles = "Admin")]
public class EmailController : ControllerBase
{
    private readonly AppDbContext _db;

    public EmailController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("reports")]
    public async Task<ActionResult<List<EmailReportStatusDto>>> Reports()
    {
        var result = new List<EmailReportStatusDto>();
        foreach (var key in EmailReports.Keys)
        {
            var task = EmailReportService.LogTaskName(key);
            var lastSent = await _db.MaintenanceLogs.Where(l => l.TaskName == task && l.Outcome == "success").MaxAsync(l => (DateTime?)l.RunAt);
            var lastError = await _db.MaintenanceLogs.Where(l => l.TaskName == task && l.Outcome == "error").OrderByDescending(l => l.RunAt).FirstOrDefaultAsync();
            result.Add(new EmailReportStatusDto(
                key, EmailReports.Title(key),
                await SettingsReader.GetStringAsync(_db, $"Email{key}To", ""),
                await SettingsReader.GetIntAsync(_db, $"Email{key}Hour", 7),
                lastSent,
                lastError is not null && (lastSent is null || lastError.RunAt > lastSent) ? lastError.Message : null,
                lastError?.RunAt));
        }

        return result;
    }

    // The report as it would be sent right now, as an HTML page.
    [HttpGet("reports/{key}/preview")]
    public async Task<IActionResult> Preview(string key)
    {
        if (!EmailReports.Keys.Contains(key))
        {
            return NotFound();
        }

        var report = await EmailReports.BuildAsync(_db, key, SiteClock.Now);
        return Content($"<!-- Subject: {System.Net.WebUtility.HtmlEncode(report.Subject)} -->{report.HtmlBody}", "text/html");
    }

    [HttpPost("reports/{key}/send-now")]
    public async Task<ActionResult<SendEmailResultDto>> SendNow(string key)
    {
        if (!EmailReports.Keys.Contains(key))
        {
            return NotFound();
        }

        var recipients = await SettingsReader.GetStringAsync(_db, $"Email{key}To", "");
        if (string.IsNullOrWhiteSpace(recipients))
        {
            return BadRequest($"Set who the {EmailReports.Title(key).ToLowerInvariant()} email goes to first.");
        }

        var (sent, message) = await EmailReportService.SendAndLogAsync(_db, key, recipients, SiteClock.Now);
        return new SendEmailResultDto(sent, message);
    }
}
