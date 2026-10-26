using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProductionScheduler.Infrastructure;

namespace ProductionScheduler.Scheduling.BackgroundServices;

// Keeps each MPS file where its job's schedule says it should be, between the overnight sends — see MpsFiles.
public class MpsFileService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MpsFileService> _logger;

    public MpsFileService(IServiceScopeFactory scopeFactory, ILogger<MpsFileService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MpsFileService poll failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    internal async Task PollAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        await MpsFiles.ReconcileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), ct);
    }
}
