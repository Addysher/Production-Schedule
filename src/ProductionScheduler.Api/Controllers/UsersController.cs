using ProductionScheduler.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Infrastructure.Auth;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;

    public UsersController(AppDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    // Role names are matched exactly by [Authorize(Roles = ...)], so anything else would create an
    // account that can sign in but can do nothing.
    // Kiosk: a factory-floor display account — it only sees the kiosk page (and, like Viewer, can't change anything).
    private static readonly string[] ValidRoles = ["Admin", "Supervisor", "Viewer", "Kiosk"];

    private static string? InvalidRole(string role) =>
        ValidRoles.Contains(role) ? null : $"Role must be one of: {string.Join(", ", ValidRoles)}.";

    private const string LastAdminMessage = "At least one active Admin is required — make someone else an Admin first.";

    private async Task<bool> IsLastActiveAdminAsync(User user) =>
        user.Role == "Admin" && user.IsActive && !await _db.Users.AnyAsync(u => u.Id != user.Id && u.Role == "Admin" && u.IsActive);

    private static UserDto ToDto(User u) => new(u.Id, u.Username, u.Role, u.IsActive, u.CreatedAt, u.LastLoginAt, u.FactoryId, u.Factory?.Name);

    // Each factory's kiosk display has its own login (v3.0): a Kiosk account must name an active factory,
    // and a factory has at most one active Kiosk account. Other roles never carry a factory.
    private async Task<string?> InvalidKioskFactoryAsync(string role, int? factoryId, bool isActive, int? userId = null)
    {
        if (role != "Kiosk" || !isActive) // a switched-off kiosk login can be left without a factory
        {
            return null;
        }

        if (factoryId is null || !await _db.Factories.AnyAsync(f => f.Id == factoryId && f.IsActive))
        {
            return "A Kiosk account needs an active factory — the one whose display it shows.";
        }

        if (await _db.Users.AnyAsync(u => u.Id != userId && u.Role == "Kiosk" && u.IsActive && u.FactoryId == factoryId))
        {
            return "That factory already has a kiosk login. Each factory has one.";
        }

        return null;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAll()
    {
        var users = await _db.Users.AsNoTracking().Include(u => u.Factory).OrderBy(u => u.Username).ToListAsync();
        return users.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<UserDto>> GetById(int id)
    {
        var user = await _db.Users.AsNoTracking().Include(u => u.Factory).FirstOrDefaultAsync(u => u.Id == id);
        return user is null ? NotFound() : ToDto(user);
    }

    [HttpPost]
    public async Task<ActionResult<UserDto>> Create(CreateUserRequest request)
    {
        if (PasswordPolicy.Validate(request.Password) is { } weak)
        {
            return BadRequest(weak);
        }

        if (InvalidRole(request.Role) is { } badRole)
        {
            return BadRequest(badRole);
        }

        if (await _db.Users.AnyAsync(u => u.Username == request.Username))
        {
            return Conflict("Username already exists.");
        }

        if (await InvalidKioskFactoryAsync(request.Role, request.FactoryId, isActive: true) is { } badFactory)
        {
            return BadRequest(badFactory);
        }

        var user = new User
        {
            Username = request.Username,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Role = request.Role,
            FactoryId = request.Role == "Kiosk" ? request.FactoryId : null,
            IsActive = true,
            CreatedAt = SiteClock.Now
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Factory).LoadAsync();
        return CreatedAtAction(nameof(GetById), new { id = user.Id }, ToDto(user));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<UserDto>> Update(int id, UpdateUserRequest request)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (InvalidRole(request.Role) is { } badRole)
        {
            return BadRequest(badRole);
        }

        if ((request.Role != "Admin" || !request.IsActive) && await IsLastActiveAdminAsync(user))
        {
            return Conflict(LastAdminMessage);
        }

        if (await InvalidKioskFactoryAsync(request.Role, request.FactoryId, request.IsActive, user.Id) is { } badFactory)
        {
            return BadRequest(badFactory);
        }

        user.Role = request.Role;
        user.IsActive = request.IsActive;
        user.FactoryId = request.Role == "Kiosk" ? request.FactoryId : null;
        await _db.SaveChangesAsync();
        await _db.Entry(user).Reference(u => u.Factory).LoadAsync();
        return ToDto(user);
    }

    [HttpPut("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, ResetPasswordRequest request)
    {
        if (PasswordPolicy.Validate(request.NewPassword) is { } weak)
        {
            return BadRequest(weak);
        }

        var user = await _db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user is null)
        {
            return NotFound();
        }

        if (await IsLastActiveAdminAsync(user))
        {
            return Conflict(LastAdminMessage);
        }

        user.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
