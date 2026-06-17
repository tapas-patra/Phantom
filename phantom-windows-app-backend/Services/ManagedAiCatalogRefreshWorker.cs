namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiCatalogRefreshWorker : BackgroundService
{
    private readonly ManagedAiCatalogService _catalogService;

    public ManagedAiCatalogRefreshWorker(ManagedAiCatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _catalogService.RefreshConfiguredProvidersAsync(stoppingToken);
            }
            catch
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(12), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
