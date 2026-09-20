namespace ProductionScheduler.Domain;

// The factory's wall clock. Shift hours, working days, PaperlessJig's YYYYMMDD.prd file names and the
// timestamps inside those files are all site-local time, so the scheduler, readers and audit stamps
// all use this instead of DateTime.UtcNow. Values are Kind.Unspecified so they serialise without an
// offset and browsers show them as-is.
//
// Defaults to the server's own time zone (the server sits at the site). Set Site:TimeZoneId in
// appsettings to pin a specific zone if the server's clock zone differs from the factory's.
// JWT expiry deliberately stays on UtcNow — that is a point in time, not a wall-clock reading.
public static class SiteClock
{
    private static TimeZoneInfo _zone = TimeZoneInfo.Local;

    public static TimeZoneInfo Zone => _zone;

    public static void UseTimeZone(string? timeZoneId)
    {
        _zone = string.IsNullOrWhiteSpace(timeZoneId) ? TimeZoneInfo.Local : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public static DateTime Now => FromUtc(DateTime.UtcNow, _zone);

    public static DateOnly Today => DateOnly.FromDateTime(Now);

    public static DateTime FromUtc(DateTime utc, TimeZoneInfo zone) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone), DateTimeKind.Unspecified);
}
