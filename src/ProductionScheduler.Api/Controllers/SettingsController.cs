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

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    // Read access is open to any authenticated user (same reasoning as calendar-settings): the
    // appearance settings (ThemePrimaryColor/ThemeSecondaryColor/ThemeMode) have to be readable by a
    // Viewer too, since every logged-in user renders the same themed UI, not just Admins.
    [HttpGet]
    public async Task<ActionResult<List<SettingDto>>> GetAll()
    {
        var settings = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
        return settings.Select(s => new SettingDto(s.Key, s.Value, s.UpdatedAt)).ToList();
    }

    [HttpPut("{key}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<SettingDto>> Update(string key, UpdateSettingRequest request)
    {
        var value = (request.Value ?? "").Trim(); // blank is valid: it means "not set up yet" for folder paths
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

        await _db.SaveChangesAsync();
        return new SettingDto(setting.Key, setting.Value, setting.UpdatedAt);
    }
}
