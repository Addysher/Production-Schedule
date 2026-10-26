using System.Net;
using System.Net.Mail;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling.BackgroundServices;

namespace ProductionScheduler.Scheduling.Email;

// Sends a report through the site's SMTP relay (Settings → Email). With EmailPickupDirectory set, the
// message is written there as an .eml file instead — for testing without a mail server.
public static class EmailSender
{
    public static async Task SendAsync(AppDbContext db, EmailReport report, string recipients, CancellationToken ct = default)
    {
        var pickup = await SettingsReader.GetStringAsync(db, "EmailPickupDirectory", "", ct);
        var host = await SettingsReader.GetStringAsync(db, "SmtpHost", "", ct);
        var from = await SettingsReader.GetStringAsync(db, "SmtpFromAddress", "", ct);

        if (string.IsNullOrWhiteSpace(from))
        {
            throw new InvalidOperationException("No From address is set (Settings → Email → From address).");
        }

        if (string.IsNullOrWhiteSpace(pickup) && string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException("No SMTP server is set (Settings → Email → SMTP server), and no pickup folder for testing.");
        }

        using var message = new MailMessage { From = new MailAddress(from), Subject = report.Subject, Body = report.HtmlBody, IsBodyHtml = true };
        foreach (var address in SplitAddresses(recipients))
        {
            message.To.Add(address);
        }

        using var client = new SmtpClient();
        if (!string.IsNullOrWhiteSpace(pickup))
        {
            Directory.CreateDirectory(pickup);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = pickup;
        }
        else
        {
            client.Host = host;
            client.Port = await SettingsReader.GetIntAsync(db, "SmtpPort", 25, ct);
            client.EnableSsl = string.Equals(await SettingsReader.GetStringAsync(db, "SmtpUseSsl", "false", ct), "true", StringComparison.OrdinalIgnoreCase);
            var username = await SettingsReader.GetStringAsync(db, "SmtpUsername", "", ct);
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, await SettingsReader.GetStringAsync(db, "SmtpPassword", "", ct));
            }
        }

        await client.SendMailAsync(message, ct);
    }

    // "a@x.com; b@x.com" or "a@x.com, b@x.com" — usually a single distribution list.
    public static IEnumerable<string> SplitAddresses(string recipients) =>
        recipients.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
