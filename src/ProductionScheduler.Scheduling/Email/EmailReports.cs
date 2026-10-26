using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.Email;

public sealed record EmailReport(string Key, string Subject, string HtmlBody, int RowCount);

// v3.0 email reports (§5.21). Each goes to one address or IT-managed distribution list, once a working day
// at an Admin-set hour. The layout is a plain HTML table for now; the final form is still to be agreed.
public static class EmailReports
{
    public const string NextDayJobs = "NextDayJobs";
    public const string ReadyForProduction = "ReadyForProduction";
    public const string DailyProduction = "DailyProduction";

    public static readonly IReadOnlyList<string> Keys = [NextDayJobs, ReadyForProduction, DailyProduction];

    public static string Title(string key) => key switch
    {
        NextDayJobs => "Next day's jobs",
        ReadyForProduction => "Jobs needing final design",
        DailyProduction => "Daily production values",
        _ => key,
    };

    public static Task<EmailReport> BuildAsync(AppDbContext db, string key, DateTime now, CancellationToken ct = default) => key switch
    {
        NextDayJobs => NextDayJobsAsync(db, now, ct),
        ReadyForProduction => ReadyForProductionAsync(db, now, ct),
        DailyProduction => DailyProductionAsync(db, now, ct),
        _ => throw new ArgumentException($"Unknown report '{key}'.", nameof(key)),
    };

    // Everything planned on the next working day, by station, in running order.
    private static async Task<EmailReport> NextDayJobsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var calendar = await WorkCalendarSettings.LoadAsync(db, ct);
        var day = NextWorkingDay(DateOnly.FromDateTime(now), calendar.WorkingDays);

        var rows = await db.ScheduleSegments
            .Where(s => s.SegmentDate == day && !s.ScheduleItem.IsArchived)
            .Select(s => new
            {
                Station = s.ScheduleItem.Station.Name,
                s.ScheduleItem.Station.DisplayOrder,
                s.SegmentStart,
                s.ScheduleItem.JobRef,
                s.ScheduleItem.Job.ClientName,
                s.ScheduleItem.Job.SiteSuburb,
                s.ScheduleItem.Job.ComponentType,
                s.PlannedEqA,
                s.ScheduleItem.Job.Status,
                s.ScheduleItem.Job.SawBundleStatus,
            })
            .ToListAsync(ct);

        var html = new StringBuilder();
        html.Append(Heading($"{Title(NextDayJobs)} — {day:dddd d MMMM yyyy}"));
        if (rows.Count == 0)
        {
            html.Append(Note("Nothing is planned for that day."));
        }

        foreach (var station in rows.GroupBy(r => (r.DisplayOrder, r.Station)).OrderBy(g => g.Key.DisplayOrder).ThenBy(g => g.Key.Station))
        {
            html.Append($"<h3 style=\"margin:18px 0 6px\">{E(station.Key.Station)} — {station.Sum(r => r.PlannedEqA):0.##} Eq</h3>");
            html.Append(Table(
                ["Start", "Job", "Client", "Suburb", "Product", "Eq that day", "Status", "Saw files"],
                station.OrderBy(r => r.SegmentStart).Select(r => new[]
                {
                    r.SegmentStart.ToString("HH:mm"), r.JobRef, r.ClientName ?? "", r.SiteSuburb ?? "", r.ComponentType ?? "",
                    r.PlannedEqA.ToString("0.##"), StatusLabel(r.Status), SawLabel(r.SawBundleStatus),
                })));
        }

        return new EmailReport(NextDayJobs, $"Production plan for {day:ddd d MMM}: {rows.Select(r => r.JobRef).Distinct().Count()} job(s)", Wrap(html), rows.Count);
    }

    // §5.21 option (a): jobs still at quote stage whose due date is within the next N days (or already
    // past) — they need their final design soon to be made in time. Quote jobs aren't scheduled, so the
    // due date (est. booked delivery, else expected delivery) is what "scheduled within N days" means.
    private static async Task<EmailReport> ReadyForProductionAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var days = Math.Clamp(await BackgroundServices.SettingsReader.GetIntAsync(db, "EmailReadyForProductionDays", 10, ct), 1, 60);
        var today = DateOnly.FromDateTime(now);
        var horizon = today.AddDays(days);

        var jobs = await db.Jobs.AsNoTracking()
            .Where(j => !j.IsArchived && j.DesignStatus == "Quote")
            .Select(j => new { j.JobRef, j.ClientName, j.SiteSuburb, j.ComponentType, j.TotalEqA, Due = j.EstBookedDeliveryDate ?? j.ExpectedDeliveryDate })
            .Where(j => j.Due != null && j.Due <= horizon)
            .OrderBy(j => j.Due)
            .ToListAsync(ct);

        var html = new StringBuilder();
        html.Append(Heading($"{Title(ReadyForProduction)} — due by {horizon:d MMMM yyyy}"));
        html.Append(Note($"Jobs still at quote stage that are due within {days} day(s). They need a final design before they can be scheduled."));
        html.Append(jobs.Count == 0
            ? Note("None — every job due in that time has its final design.")
            : Table(
                ["Due", "Days left", "Job", "Client", "Suburb", "Product", "Eq"],
                jobs.Select(j => new[]
                {
                    j.Due!.Value.ToString("ddd d MMM"), (j.Due!.Value.DayNumber - today.DayNumber) is var left && left < 0 ? $"{-left} overdue" : left.ToString(),
                    j.JobRef, j.ClientName ?? "", j.SiteSuburb ?? "", j.ComponentType ?? "", j.TotalEqA?.ToString("0.##") ?? "",
                })));

        return new EmailReport(ReadyForProduction, $"{jobs.Count} quote job(s) due within {days} days need a final design", Wrap(html), jobs.Count);
    }

    // What each station actually made today (from member completions, like the Reports page) against
    // its target for the day, plus the jobs finished today.
    private static async Task<EmailReport> DailyProductionAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now);
        var start = today.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var made = await (
                from m in db.JobMembers
                join j in db.Jobs on m.JobRef equals j.JobRef
                where m.Done && m.CompletedAt >= start && m.CompletedAt < end && j.AssignedStationId != null
                group (m.CompletedEqA ?? m.EqaValue) by j.AssignedStationId!.Value into g
                select new { StationId = g.Key, Eqa = g.Sum() })
            .ToDictionaryAsync(r => r.StationId, r => r.Eqa, ct);

        var stations = await db.Stations.AsNoTracking().Where(s => s.IsActive || made.Keys.Contains(s.Id)).OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name).ToListAsync(ct);
        // Each line's target for the day: its EqA per hour × today's working hours (with any one-off change).
        var calendar = await WorkCalendarSettings.LoadAsync(db, ct);
        var changes = await db.StationCapacityOverrides.AsNoTracking().Where(o => o.OverrideDate == today).ToDictionaryAsync(o => o.StationId, ct);

        var finished = await db.ScheduleItems.AsNoTracking()
            .Where(i => i.ActualEnd >= start && i.ActualEnd < end)
            .OrderBy(i => i.ActualEnd)
            .Select(i => new { i.JobRef, Station = i.Station.Name, i.Job.ClientName, i.EqaCompleted, i.ActualEnd })
            .ToListAsync(ct);

        var html = new StringBuilder();
        html.Append(Heading($"{Title(DailyProduction)} — {today:dddd d MMMM yyyy}"));
        var stationRows = stations.Select(s =>
        {
            var eqa = made.GetValueOrDefault(s.Id);
            var window = calendar.WindowFor(changes.TryGetValue(s.Id, out var o) ? new Dictionary<DateOnly, Domain.Entities.StationCapacityOverride> { [today] = o } : null)(today);
            decimal? target = s.EqaPerHour is > 0 ? (decimal)calendar.DailyTargetEqa((double)s.EqaPerHour.Value, window) : null;
            return new[]
            {
                s.Name, eqa.ToString("0.##"), target?.ToString("0.##") ?? "—",
                target is > 0 ? $"{eqa / target.Value * 100:0}%" : "—",
            };
        }).ToList();
        var total = made.Values.Sum();
        stationRows.Add(["Total", total.ToString("0.##"), "", ""]);
        html.Append(Table(["Station", "Made (Eq)", "Target (Eq)", "Of target"], stationRows));

        html.Append("<h3 style=\"margin:18px 0 6px\">Jobs finished today</h3>");
        html.Append(finished.Count == 0
            ? Note("None.")
            : Table(["Finished", "Job", "Station", "Client", "Eq"],
                finished.Select(f => new[] { f.ActualEnd!.Value.ToString("HH:mm"), f.JobRef, f.Station, f.ClientName ?? "", f.EqaCompleted.ToString("0.##") })));

        return new EmailReport(DailyProduction, $"Production {today:ddd d MMM}: {total:0.##} Eq made, {finished.Count} job(s) finished", Wrap(html), stationRows.Count - 1 + finished.Count);
    }

    internal static DateOnly NextWorkingDay(DateOnly from, IReadOnlySet<int> workingDays)
    {
        var day = from.AddDays(1);
        for (var i = 0; i < 14 && !workingDays.Contains((int)day.DayOfWeek); i++)
        {
            day = day.AddDays(1);
        }

        return day;
    }

    private static string StatusLabel(string status) => status switch
    {
        "ready" => "Ready for production",
        "approved" => "Approved",
        "cut" => "Cut and ready",
        "in-production" => "In production",
        "completed" => "Completed",
        _ => status,
    };

    private static string SawLabel(string? bundle) => bundle switch
    {
        "bundling" => "Incomplete",
        "waiting" => "Ready to approve",
        "cutting" => "With the saw",
        _ => "None yet",
    };

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Heading(string text) => $"<h2 style=\"margin:0 0 8px;color:#0a5e73\">{E(text)}</h2>";

    private static string Note(string text) => $"<p style=\"margin:6px 0;color:#475569\">{E(text)}</p>";

    private static string Table(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder("<table style=\"border-collapse:collapse;font-size:13px\"><thead><tr>");
        foreach (var h in headers)
        {
            sb.Append($"<th style=\"text-align:left;padding:4px 10px;border-bottom:2px solid #0a5e73\">{E(h)}</th>");
        }

        sb.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row)
            {
                sb.Append($"<td style=\"padding:4px 10px;border-bottom:1px solid #e2e8f0\">{E(cell)}</td>");
            }

            sb.Append("</tr>");
        }

        return sb.Append("</tbody></table>").ToString();
    }

    private static string Wrap(StringBuilder body) =>
        $"<!doctype html><html><body style=\"font-family:Segoe UI,Arial,sans-serif;color:#0f172a\">{body}" +
        "<p style=\"margin-top:20px;font-size:11px;color:#94a3b8\">Sent by the Production Scheduler.</p></body></html>";
}
