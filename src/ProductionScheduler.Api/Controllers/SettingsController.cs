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
[Authorize(Roles = "Admin")]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;

    public SettingsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<SettingDto>>> GetAll()
    {
        var settings = await _db.Settings.AsNoTracking().OrderBy(s => s.Key).ToListAsync();
        return settings.Select(s => new SettingDto(s.Key, s.Value, s.UpdatedAt)).ToList();
    }

    [HttpPut("{key}")]
    public async Task<ActionResult<SettingDto>> Update(string key, UpdateSettingRequest request)
    {
        var setting = await _db.Settings.FindAsync(key);
        var now = SiteClock.Now;

        if (setting is null)
        {
            setting = new Setting { Key = key, Value = request.Value, UpdatedAt = now };
            _db.Settings.Add(setting);
        }
        else
        {
            setting.Value = request.Value;
            setting.UpdatedAt = now;
        }

        await _db.SaveChangesAsync();
        return new SettingDto(setting.Key, setting.Value, setting.UpdatedAt);
    }
}
