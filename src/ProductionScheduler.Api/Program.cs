using ProductionScheduler.Domain;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ProductionScheduler.Domain.Entities;
using ProductionScheduler.Infrastructure;
using ProductionScheduler.Infrastructure.Auth;
using ProductionScheduler.Scheduling.BackgroundServices;
using ProductionScheduler.Scheduling;
using ProductionScheduler.Scheduling.ConstraintResolution;

var builder = WebApplication.CreateBuilder(args);

// Empty = the server's own time zone. See SiteClock.
SiteClock.UseTimeZone(builder.Configuration["Site:TimeZoneId"]);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services.AddScoped<ISetupResolver, SetupResolver>();
builder.Services.AddScoped<IScheduleGenerationService, ScheduleGenerationService>();
builder.Services.AddScoped<IJobStatusService, JobStatusService>();

builder.Services.AddHostedService<MmeFileWatcherService>();
builder.Services.AddHostedService<SchImportService>();
builder.Services.AddHostedService<BatchcutWatcherService>();
builder.Services.AddHostedService<PrdReaderService>();
builder.Services.AddHostedService<NightlyMaintenanceService>();

var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key))
        };
    });

// Every endpoint requires a valid token unless marked [AllowAnonymous] (e.g. login).
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var seedScope = app.Services.CreateScope())
{
    // Development applies pending migrations on start, so pulling new code never leaves a stale schema behind.
    if (app.Environment.IsDevelopment())
    {
        await seedScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    await DatabaseSeeder.SeedDefaultsAsync(seedScope.ServiceProvider.GetRequiredService<AppDbContext>());
}

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    if (!await db.Users.AnyAsync())
    {
        db.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = hasher.Hash("Admin123!"),
            Role = "Admin",
            IsActive = true,
            CreatedAt = SiteClock.Now
        });
        await db.SaveChangesAsync();
        Console.WriteLine("Seeded default dev admin account: admin / Admin123!");
    }
}

app.Run();

// Exposes the entry point to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
