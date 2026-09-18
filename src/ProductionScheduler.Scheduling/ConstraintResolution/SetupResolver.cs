using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.ConstraintResolution;

public interface ISetupResolver
{
    Task<int> GetSetupMinutesAsync(string fromComponentType, string toComponentType, CancellationToken ct = default);
}

// Keyed on (FromComponentType, ToComponentType) per §4.2 — changeover cost is shared across all
// stations of a component type, so no machineId is involved despite §3.2's mention of one.
public class SetupResolver : ISetupResolver
{
    private readonly AppDbContext _db;

    public SetupResolver(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetSetupMinutesAsync(string fromComponentType, string toComponentType, CancellationToken ct = default)
    {
        var setupTime = await _db.SetupTimes
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.FromComponentType == fromComponentType && s.ToComponentType == toComponentType, ct);

        return setupTime?.DurationMins ?? 0;
    }
}
