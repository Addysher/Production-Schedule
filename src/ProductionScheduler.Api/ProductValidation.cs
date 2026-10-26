using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api;

// Stations, job types and jobs name their product by Products.Name; a typo would silently create a
// product nothing else matches (a station no job can ever be routed to).
public static class ProductValidation
{
    public static async Task<string?> UnknownProductAsync(AppDbContext db, string? name) =>
        name is null || await db.Products.AnyAsync(p => p.Name == name)
            ? null
            : $"'{name}' isn't a product. Add it in Settings → Products first.";
}
