using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ProductionScheduler.Scheduling.IScheduleGenerationService _scheduleGenerationService;

    // Settings that change the shape of the working calendar: the plan is redone when one is saved.
    private static readonly HashSet<string> CalendarKeys = ["ShiftStartHour", "ShiftEndHour", "WorkingDays", "FreezeWindowDays", "ProductionLeadDaysMin", "ProductionLeadDaysMax", "PencilTbaJobs"];

    public SettingsController(AppDbContext db, ProductionScheduler.Scheduling.IScheduleGenerationService scheduleGenerationService)
    {
        _db = db;
        _scheduleGenerationService = scheduleGenerationService;
    }

    // Shown instead of a secret's value. Saving it back unchanged keeps the stored secret.
    public const string SecretMask = "********";

    private static bool IsSecret(string key) => key.EndsWith("Password", StringComparison.OrdinalIgnoreCase);

    // Read access is open to any authenticated user (same reasoning as calendar-settings): the
    // appearance settings (ThemePrimaryColor/ThemeSecondaryColor/ThemeMode) have to be readable by a
    // Viewer too, since every logged-in user renders the same themed UI. That is all they get, though —
    // folder paths and mail settings are Admin business — and secrets are never sent to anyone.
    [HttpGet]
    public async Task<ActionResult<List<SettingDto>>> GetAll()
    {
        var query = _db.Settings.AsNoTracking();
        if (!User.IsInRole("Admin"))
        {
            query = query.Where(s => s.Key.StartsWith("Theme"));
        }

        var settings = await query.OrderBy(s => s.Key).ToListAsync();
        return settings.Select(ToDto).ToList();
    }

    private static SettingDto ToDto(Setting s) =>
        new(s.Key, IsSecret(s.Key) && s.Value.Length > 0 ? SecretMask : s.Value, s.UpdatedAt);

    [HttpPut("{key}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SettingDto>> Update(string key, UpdateSettingRequest request)
    {
        var value = (request.Value ?? "").Trim(); // blank is valid: it means "not set up yet" for folder paths
        if (IsSecret(key) && value == SecretMask)
        {
            var unchanged = await _db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key);
            return unchanged is null ? NotFound() : ToDto(unchanged);
        }

        if (IsSecret(key))
        {
            value = request.Value ?? ""; // a password may legitimately start or end with a space
        }
        if (await SettingValidation.ProblemAsync(_db, key, value) is { } problem)
        {
            return BadRequest(problem);
        }

        var setting = await _db.Settings.FindAsync(key);
        var now = SiteClock.Now;

        if (setting is null)
        {
            setting = new Setting { Key = key, Value = value, UpdatedAt = now };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }

        // A calendar change is saved and re-planned together, and undone if it leaves no working time.
        if (CalendarKeys.Contains(key))
        {
            if (await _scheduleGenerationService.SaveAndReplanAsync() is { } noWorkingTime)
            {
                return BadRequest(noWorkingTime);
            }
        }
        else
        {
            await _db.SaveChangesAsync();
        }

        return ToDto(setting);
    }
}
