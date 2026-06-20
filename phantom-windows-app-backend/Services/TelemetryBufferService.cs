using System.Threading.Channels;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class TelemetryBufferService : BackgroundService
{
    private const int BatchSize = 100;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    private readonly Channel<TelemetryEventRecord> _channel;
    private readonly TelemetryRepository _repository;
    private readonly OperationalMetricsService _metrics;
    private readonly ILogger<TelemetryBufferService> _logger;

    public TelemetryBufferService(
        TelemetryRepository repository,
        OperationalMetricsService metrics,
        ILogger<TelemetryBufferService> logger)
    {
        _repository = repository;
        _metrics = metrics;
        _logger = logger;
        _channel = Channel.CreateBounded<TelemetryEventRecord>(new BoundedChannelOptions(10_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(TelemetryEventRecord record)
    {
        var accepted = _channel.Writer.TryWrite(record);
        if (accepted)
        {
            _metrics.RecordTelemetryQueued();
        }
        else
        {
            _metrics.RecordTelemetryDropped();
        }

        return accepted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<TelemetryEventRecord>(BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var readTask = _channel.Reader.ReadAsync(stoppingToken).AsTask();
                var completed = await Task.WhenAny(readTask, Task.Delay(FlushInterval, stoppingToken));
                if (completed == readTask)
                {
                    batch.Add(await readTask);
                    while (batch.Count < BatchSize && _channel.Reader.TryRead(out var buffered))
                    {
                        batch.Add(buffered);
                    }
                }

                if (batch.Count == 0)
                {
                    continue;
                }

                await FlushBatchWithRetry(batch, stoppingToken);
                batch.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        if (batch.Count > 0)
        {
            await FlushBatchWithRetry(batch, CancellationToken.None);
        }
    }

    private async Task FlushBatchWithRetry(List<TelemetryEventRecord> batch, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                _repository.SaveBatch(batch);
                FlushSuccess(batch.Count);
                return;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _metrics.RecordTelemetryFlushFailed();
                _logger.LogWarning(ex, "Telemetry batch flush failed. Retrying.");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }
    }

    private void FlushSuccess(int count)
    {
        _metrics.RecordTelemetryFlushSucceeded(count);
    }
}
