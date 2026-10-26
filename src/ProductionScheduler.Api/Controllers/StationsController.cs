using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Scheduling;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/stations")]
public class StationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IScheduleGenerationService _scheduleGenerationService;

    public StationsController(AppDbContext db, IScheduleGenerationService scheduleGenerationService)
    {
        _db = db;
        _scheduleGenerationService = scheduleGenerationService;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Today's working hours for each line (with any one-off change) give today's target: EqA/h × hours.
    private async Task<Func<Station, StationDto>> ToDtoAsync()
    {
        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        var today = DateOnly.FromDateTime(SiteClock.Now);
        var changesToday = await _db.StationCapacityOverrides.AsNoTracking().Where(o => o.OverrideDate == today).ToDictionaryAsync(o => o.StationId);
        return s =>
        {
            var change = changesToday.GetValueOrDefault(s.Id);
            var window = calendar.WindowFor(change is null ? null : new Dictionary<DateOnly, StationCapacityOverride> { [today] = change })(today);
            decimal? target = s.EqaPerHour is > 0 ? Math.Round((decimal)calendar.DailyTargetEqa((double)s.EqaPerHour.Value, window), 1) : null;
            return new StationDto(s.Id, s.Name, s.ComponentType, s.PrdPath, s.MpsPath, s.IsActive, s.DisplayOrder, s.EqaPerHour, s.ArchivePath, s.FactoryId,
                target, change is null ? null : Describe(change));
        };
    }

    private static string Describe(StationCapacityOverride o) =>
        o.IsClosed ? $"Closed{(o.Note is null ? "" : $" — {o.Note}")}"
        : $"{o.StartTime?.ToString("HH:mm") ?? "normal start"}–{(o.EndTime == TimeOnly.MinValue ? "24:00" : o.EndTime?.ToString("HH:mm") ?? "normal end")}{(o.Note is null ? "" : $" — {o.Note}")}";

    private static CapacityOverrideDto ToOverrideDto(StationCapacityOverride o) =>
        new(o.Id, o.StationId, o.OverrideDate, o.IsClosed, o.StartTime, o.EndTime, o.Note);

    // Saves, then re-plans in the same transaction: a change that leaves a line no working time for months
    // is undone with the reason instead of breaking every later re-plan.
    private Task<string?> SaveAndReplanAsync() => _scheduleGenerationService.SaveAndReplanAsync();

    // Read access is open to any authenticated user — the calendar and dashboard (Viewer-visible)
    // render station columns/cards, even though station management itself is Admin-only.
    [HttpGet]
    public async Task<ActionResult<List<StationDto>>> GetAll()
    {
        var stations = await _db.Stations.AsNoTracking().OrderBy(s => s.DisplayOrder).ToListAsync();
        var toDto = await ToDtoAsync();
        return stations.Select(toDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<StationDto>> GetById(int id)
    {
        var station = await _db.Stations.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return station is null ? NotFound() : (await ToDtoAsync())(station);
    }

    private async Task<string?> ProblemAsync(string componentType, int? factoryId, decimal? eqaPerHour)
    {
        if (await ProductValidation.UnknownProductAsync(_db, componentType) is { } unknown)
        {
            return unknown;
        }

        if (factoryId is { } id && !await _db.Factories.AnyAsync(f => f.Id == id))
        {
            return $"Factory {id} doesn't exist.";
        }

        return eqaPerHour is <= 0 ? "Capacity must be more than 0 EqA per hour (or left blank)." : null;
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StationDto>> Create(CreateStationRequest request)
    {
        if (await ProblemAsync(request.ComponentType, request.FactoryId, request.EqaPerHour) is { } problem)
        {
            return BadRequest(problem);
        }

        var station = new Station
        {
            Name = request.Name,
            ComponentType = request.ComponentType,
            PrdPath = Blank(request.PrdPath),
            MpsPath = Blank(request.MpsPath),
            DisplayOrder = request.DisplayOrder,
            EqaPerHour = request.EqaPerHour,
            ArchivePath = Blank(request.ArchivePath),
            FactoryId = request.FactoryId,
            IsActive = true
        };
        _db.Stations.Add(station);
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return CreatedAtAction(nameof(GetById), new { id = station.Id }, (await ToDtoAsync())(station));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<StationDto>> Update(int id, UpdateStationRequest request)
    {
        var station = await _db.Stations.FindAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        if (await ProblemAsync(request.ComponentType, request.FactoryId, request.EqaPerHour) is { } problem)
        {
            return BadRequest(problem);
        }

        station.Name = request.Name;
        station.ComponentType = request.ComponentType;
        station.PrdPath = Blank(request.PrdPath);
        station.MpsPath = Blank(request.MpsPath);
        station.IsActive = request.IsActive;
        station.DisplayOrder = request.DisplayOrder;
        station.EqaPerHour = request.EqaPerHour;
        station.ArchivePath = Blank(request.ArchivePath);
        station.FactoryId = request.FactoryId;
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return (await ToDtoAsync())(station);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var station = await _db.Stations.FindAsync(id);
        if (station is null)
        {
            return NotFound();
        }

        // Deactivate rather than hard-delete — stations are referenced by historical ScheduleItems.
        station.IsActive = false;
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return NoContent();
    }

    // --- One-off changes to a line's working day: overtime hours, or closed (shutdown / public holiday) ---

    // A missing start or end means the normal one, so the check is on the hours the line would actually work.
    private async Task<string?> OverrideProblemAsync(UpsertCapacityOverrideRequest request)
    {
        if (request.IsClosed)
        {
            return null;
        }

        if (request.StartTime is null && request.EndTime is null)
        {
            return "Give the overtime hours (a start and/or end time), or mark the line closed.";
        }

        var calendar = await WorkCalendarSettings.LoadAsync(_db);
        var start = request.StartTime?.ToTimeSpan() ?? calendar.DayStart;
        var end = request.EndTime is { } e ? (e == TimeOnly.MinValue ? TimeSpan.FromHours(24) : e.ToTimeSpan()) : calendar.DayEnd;
        return end <= start
            ? $"Those hours ({Clock(start)} to {Clock(end)}) leave no working time; the end must be after the start."
            : null;
    }

    private static string Clock(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    private static void Apply(StationCapacityOverride entity, UpsertCapacityOverrideRequest request)
    {
        entity.OverrideDate = request.OverrideDate;
        entity.IsClosed = request.IsClosed;
        entity.StartTime = request.IsClosed ? null : request.StartTime;
        entity.EndTime = request.IsClosed ? null : request.EndTime;
        entity.Note = Blank(request.Note);
    }

    // Readable by every role: the Dashboard shows today's change on each line to Viewers too. Writes stay Admin-only.
    [HttpGet("{id:int}/capacity-overrides")]
    public async Task<ActionResult<List<CapacityOverrideDto>>> GetCapacityOverrides(int id)
    {
        var overrides = await _db.StationCapacityOverrides
            .AsNoTracking()
            .Where(o => o.StationId == id)
            .OrderBy(o => o.OverrideDate)
            .ToListAsync();
        return overrides.Select(ToOverrideDto).ToList();
    }

    [HttpPost("{id:int}/capacity-overrides")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CapacityOverrideDto>> CreateCapacityOverride(int id, UpsertCapacityOverrideRequest request)
    {
        if (!await _db.Stations.AnyAsync(s => s.Id == id))
        {
            return NotFound();
        }

        if (await OverrideProblemAsync(request) is { } problem)
        {
            return BadRequest(problem);
        }

        if (await _db.StationCapacityOverrides.AnyAsync(o => o.StationId == id && o.OverrideDate == request.OverrideDate))
        {
            return Conflict($"This line already has a change on {request.OverrideDate:ddd d MMM}; edit or remove that one.");
        }

        var entity = new StationCapacityOverride { StationId = id, CreatedAt = SiteClock.Now };
        Apply(entity, request);
        _db.StationCapacityOverrides.Add(entity);
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return ToOverrideDto(entity);
    }

    // A public holiday or site shutdown: the same change on every active line at once (replacing any change
    // those lines already had that day).
    [HttpPost("capacity-overrides/all-lines")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<CapacityOverrideDto>>> CreateForAllLines(UpsertCapacityOverrideRequest request)
    {
        if (await OverrideProblemAsync(request) is { } problem)
        {
            return BadRequest(problem);
        }

        var lineIds = await _db.Stations.Where(s => s.IsActive).Select(s => s.Id).ToListAsync();
        var existing = await _db.StationCapacityOverrides.Where(o => o.OverrideDate == request.OverrideDate && lineIds.Contains(o.StationId)).ToListAsync();
        var result = new List<StationCapacityOverride>();
        foreach (var lineId in lineIds)
        {
            var entity = existing.FirstOrDefault(o => o.StationId == lineId);
            if (entity is null)
            {
                entity = new StationCapacityOverride { StationId = lineId, CreatedAt = SiteClock.Now };
                _db.StationCapacityOverrides.Add(entity);
            }

            Apply(entity, request);
            result.Add(entity);
        }

        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return result.Select(ToOverrideDto).ToList();
    }

    [HttpPut("{id:int}/capacity-overrides/{overrideId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CapacityOverrideDto>> UpdateCapacityOverride(int id, int overrideId, UpsertCapacityOverrideRequest request)
    {
        var entity = await _db.StationCapacityOverrides.FirstOrDefaultAsync(o => o.Id == overrideId && o.StationId == id);
        if (entity is null)
        {
            return NotFound();
        }

        if (await OverrideProblemAsync(request) is { } problem)
        {
            return BadRequest(problem);
        }

        if (await _db.StationCapacityOverrides.AnyAsync(o => o.StationId == id && o.OverrideDate == request.OverrideDate && o.Id != overrideId))
        {
            return Conflict($"This line already has a change on {request.OverrideDate:ddd d MMM}; edit or remove that one.");
        }

        Apply(entity, request);
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return ToOverrideDto(entity);
    }

    [HttpDelete("{id:int}/capacity-overrides/{overrideId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCapacityOverride(int id, int overrideId)
    {
        var entity = await _db.StationCapacityOverrides.FirstOrDefaultAsync(o => o.Id == overrideId && o.StationId == id);
        if (entity is null)
        {
            return NotFound();
        }

        _db.StationCapacityOverrides.Remove(entity);
        if (await SaveAndReplanAsync() is { } noWorkingTime)
        {
            return BadRequest(noWorkingTime);
        }

        return NoContent();
    }
}
