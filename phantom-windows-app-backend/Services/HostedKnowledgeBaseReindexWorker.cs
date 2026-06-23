namespace Phantom.WindowsApp.Backend.Services;

public sealed class HostedKnowledgeBaseReindexWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HostedKnowledgeBaseReindexWorker> _logger;

    public HostedKnowledgeBaseReindexWorker(
        IServiceProvider serviceProvider,
        ILogger<HostedKnowledgeBaseReindexWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            scope.ServiceProvider.GetRequiredService<HostedKnowledgeBaseReindexJobRepository>().RequeueRunningJobs();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover hosted KB reindex jobs on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var knowledgeBases = scope.ServiceProvider.GetRequiredService<HostedKnowledgeBaseService>();
                var processed = await knowledgeBases.TryProcessNextReindexJobAsync(stoppingToken);
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
                _logger.LogError(ex, "Hosted KB reindex worker cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
