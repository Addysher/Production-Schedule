using ProductionScheduler.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductionScheduler.Api.Dtos;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Infrastructure.Auth;

namespace ProductionScheduler.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthController(AppDbContext db, IPasswordHasher passwordHasher, IJwtTokenService jwtTokenService)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Username == request.Username && u.IsActive);
        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized();
        }

        user.LastLoginAt = SiteClock.Now;
        await _db.SaveChangesAsync();

        var token = _jwtTokenService.GenerateToken(user);
        return Ok(new LoginResponse(token, user.Username, user.Role));
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // Stateless JWT — the token carries no server-side session to revoke.
        // The client is responsible for discarding it.
        return Ok();
    }

    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        if (PasswordPolicy.Validate(request.NewPassword) is { } weak)
        {
            return BadRequest(weak);
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return BadRequest("Current password is incorrect.");
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
