using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, byte> _recentEventIds = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _recentEventOrder = new();

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

    public bool TryEnqueue(TelemetryEventRecord record, out bool duplicate)
    {
        duplicate = !_recentEventIds.TryAdd(record.EventId, 0);
        if (duplicate) return true;

        _recentEventOrder.Enqueue(record.EventId);
        while (_recentEventIds.Count > 20_000 && _recentEventOrder.TryDequeue(out var expired))
            _recentEventIds.TryRemove(expired, out _);

        var accepted = _channel.Writer.TryWrite(record);
        if (accepted)
        {
            _metrics.RecordTelemetryQueued();
        }
        else
        {
            _recentEventIds.TryRemove(record.EventId, out _);
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
                batch.Add(await _channel.Reader.ReadAsync(stoppingToken));
                await Task.Delay(FlushInterval, stoppingToken);
                while (batch.Count < BatchSize && _channel.Reader.TryRead(out var buffered))
                {
                    batch.Add(buffered);
                }

                await FlushBatchWithRetry(batch, stoppingToken);
                batch.Clear();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        while (_channel.Reader.TryRead(out var buffered))
        {
            batch.Add(buffered);
            if (batch.Count < BatchSize) continue;
            await FlushBatchWithRetry(batch, CancellationToken.None);
            batch.Clear();
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
        _logger.LogInformation(
            "telemetry_batch_flushed service={Service} component={Component} event={Event} batch_count={BatchCount} outcome={Outcome}",
            "phantom-windows-app-backend", "telemetry", "telemetry_batch_flushed", count, "success");
    }
}
