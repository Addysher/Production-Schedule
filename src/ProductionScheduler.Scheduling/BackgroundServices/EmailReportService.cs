using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Domain;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.Email;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// §5.21 — sends each email report once per working day, at or after its Admin-set hour. A report with no
// recipient is switched off. Every attempt is written to MaintenanceLogs (task "Email{Report}"), which is
// also how a send is remembered: a success today means done; three failures today means give up until
// tomorrow (Settings → Email → Send now still works) rather than retrying every minute.
public class EmailReportService : BackgroundService
{
    internal const int MaxAttemptsPerDay = 3;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailReportService> _logger;

    public EmailReportService(IServiceScopeFactory scopeFactory, ILogger<EmailReportService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EmailReportService poll failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = SiteClock.Now;

        var calendar = await WorkCalendarSettings.LoadAsync(db, ct);
        if (!calendar.WorkingDays.Contains((int)now.DayOfWeek))
        {
            return;
        }

        foreach (var key in EmailReports.Keys)
        {
            var recipients = await SettingsReader.GetStringAsync(db, $"Email{key}To", "", ct);
            var hour = Math.Clamp(await SettingsReader.GetIntAsync(db, $"Email{key}Hour", 7, ct), 0, 23);
            if (string.IsNullOrWhiteSpace(recipients) || now.Hour < hour)
            {
                continue;
            }

            var task = LogTaskName(key);
            var today = now.Date;
            var attempts = await db.MaintenanceLogs
                .Where(l => l.TaskName == task && l.RunAt >= today)
                .GroupBy(l => l.Outcome)
                .Select(g => new { Outcome = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Outcome, g => g.Count, ct);
            if (attempts.ContainsKey("success") || attempts.GetValueOrDefault("error") >= MaxAttemptsPerDay)
            {
                continue;
            }

            await SendAndLogAsync(db, key, recipients, now, manual: false, ct);
        }
    }

    // Shared with Settings → Email → Send now. A manual send is logged under its own task name, so a test
    // in the morning doesn't count as (or use up the retries of) that day's scheduled send.
    public static async Task<(bool Sent, string Message)> SendAndLogAsync(AppDbContext db, string key, string recipients, DateTime now, bool manual, CancellationToken ct = default)
    {
        string message;
        var sent = false;
        try
        {
            var report = await EmailReports.BuildAsync(db, key, now, ct);
            await EmailSender.SendAsync(db, report, recipients, ct);
            sent = true;
            message = $"Sent '{report.Subject}' to {recipients}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            message = $"Couldn't send the {EmailReports.Title(key).ToLowerInvariant()} email to {recipients}: {(ex.InnerException ?? ex).Message}";
        }

        MaintenanceLogWriter.Add(db, manual ? ManualLogTaskName(key) : LogTaskName(key), sent ? "success" : "error", message);
        await db.SaveChangesAsync(ct);
        return (sent, message);
    }

    public static string LogTaskName(string key) => $"Email{key}";

    public static string ManualLogTaskName(string key) => $"Email{key}Manual";
}
