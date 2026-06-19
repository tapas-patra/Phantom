using Microsoft.Extensions.Hosting;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class MaintenanceService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly OperationalMetricsService _metrics;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(
        IServiceProvider serviceProvider,
        OperationalMetricsService metrics,
        ILogger<MaintenanceService> logger)
    {
        _serviceProvider = serviceProvider;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                scope.ServiceProvider.GetRequiredService<AuthSessionRepository>().DeleteExpired();
                scope.ServiceProvider.GetRequiredService<MagicLinkRepository>().DeleteExpired();
                scope.ServiceProvider.GetRequiredService<LockRepository>().DeleteExpired();
                scope.ServiceProvider.GetRequiredService<LoginAttemptRepository>()
                    .DeleteExpired(DateTime.UtcNow.AddDays(-2));
                _metrics.RecordMaintenanceSucceeded();
            }
            catch (Exception ex)
            {
                _metrics.RecordMaintenanceFailed();
                _logger.LogError(ex, "Background maintenance cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
