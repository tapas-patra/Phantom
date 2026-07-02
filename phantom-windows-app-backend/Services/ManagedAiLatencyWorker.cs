namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiLatencyWorker : BackgroundService
{
    private readonly ManagedAiDiagnosticsService _diagnostics;
    private readonly ILogger<ManagedAiLatencyWorker> _logger;

    public ManagedAiLatencyWorker(
        ManagedAiDiagnosticsService diagnostics,
        ILogger<ManagedAiLatencyWorker> logger)
    {
        _diagnostics = diagnostics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _diagnostics.RequeueRunningJobs();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover managed AI latency jobs on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _diagnostics.TryProcessNextLatencyRunAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Managed AI latency worker cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
