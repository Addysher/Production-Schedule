using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Api.Controllers;

// v3.0 — products and their pool folders (replaces SCH import paths). A product's Name is the key
// stored in Stations/JobTypes/Jobs.ComponentType, so renaming one renames it everywhere.
[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProductsController(AppDbContext db)
    {
        _db = db;
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Name, p.PoolPath, p.ArchivePath, p.IsActive);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // Readable by every role: product names label jobs and stations throughout the UI.
    [HttpGet]
    public async Task<ActionResult<List<ProductDto>>> GetAll() =>
        (await _db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync()).Select(ToDto).ToList();

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> Create(UpsertProductRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Product name is required.");
        }

        if (await _db.Products.AnyAsync(p => p.Name == name))
        {
            return Conflict($"A product called '{name}' already exists.");
        }

        var product = new Product { Name = name, PoolPath = Blank(request.PoolPath), ArchivePath = Blank(request.ArchivePath), IsActive = request.IsActive };
        _db.Products.Add(product);
        await _db.SaveChangesAsync();
        return ToDto(product);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductDto>> Update(int id, UpsertProductRequest request)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return BadRequest("Product name is required.");
        }

        if (await _db.Products.AnyAsync(p => p.Id != id && p.Name == name))
        {
            return Conflict($"A product called '{name}' already exists.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        var oldName = product.Name;
        if (oldName != name)
        {
            await _db.Stations.Where(s => s.ComponentType == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.ComponentType, name));
            await _db.JobTypes.Where(j => j.ComponentType == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.ComponentType, name));
            await _db.Jobs.Where(j => j.ComponentType == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.ComponentType, name));
            await _db.SetupTimes.Where(t => t.FromComponentType == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.FromComponentType, name));
            await _db.SetupTimes.Where(t => t.ToComponentType == oldName).ExecuteUpdateAsync(s => s.SetProperty(x => x.ToComponentType, name));
        }

        product.Name = name;
        product.PoolPath = Blank(request.PoolPath);
        product.ArchivePath = Blank(request.ArchivePath);
        product.IsActive = request.IsActive;
        await _db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(product);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var product = await _db.Products.FindAsync(id);
        if (product is null)
        {
            return NotFound();
        }

        var inUse = await _db.Stations.AnyAsync(s => s.ComponentType == product.Name)
            || await _db.JobTypes.AnyAsync(j => j.ComponentType == product.Name)
            || await _db.Jobs.AnyAsync(j => j.ComponentType == product.Name);
        if (inUse)
        {
            return Conflict($"'{product.Name}' is still used by stations, job types or jobs — deactivate it instead.");
        }

        _db.Products.Remove(product);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
