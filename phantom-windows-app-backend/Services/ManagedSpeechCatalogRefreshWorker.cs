namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedSpeechCatalogRefreshWorker : BackgroundService
{
    private readonly ManagedSpeechCatalogService _catalog;
    public ManagedSpeechCatalogRefreshWorker(ManagedSpeechCatalogService catalog) => _catalog = catalog;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await _catalog.RefreshConfiguredProvidersAsync(stoppingToken); } catch (OperationCanceledException) { break; } catch { }
            try { await Task.Delay(TimeSpan.FromHours(12), stoppingToken); } catch (OperationCanceledException) { break; }
        }
    }
}
