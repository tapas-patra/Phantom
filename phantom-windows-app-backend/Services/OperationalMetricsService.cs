namespace Phantom.WindowsApp.Backend.Services;

public sealed class OperationalMetricsService
{
    private long _telemetryQueued;
    private long _telemetryDropped;
    private long _telemetryFlushed;
    private long _telemetryConsecutiveFailures;
    private DateTime? _telemetryLastFlushAtUtc;
    private DateTime? _telemetryLastFailureAtUtc;

    private long _projectionApplied;
    private long _projectionConsecutiveFailures;
    private long _projectionLastBatchSize;
    private long _projectionPendingCount;
    private DateTime? _projectionOldestPendingAtUtc;
    private DateTime? _projectionLastAppliedAtUtc;
    private DateTime? _projectionLastFailureAtUtc;
    private string _projectionMode = "disabled";

    private long _maintenanceConsecutiveFailures;
    private DateTime? _maintenanceLastRunAtUtc;
    private DateTime? _maintenanceLastFailureAtUtc;

    public void RecordTelemetryQueued()
    {
        Interlocked.Increment(ref _telemetryQueued);
    }

    public void RecordTelemetryDropped()
    {
        Interlocked.Increment(ref _telemetryDropped);
    }

    public void RecordTelemetryFlushSucceeded(int flushedCount)
    {
        Interlocked.Add(ref _telemetryFlushed, flushedCount);
        Interlocked.Exchange(ref _telemetryConsecutiveFailures, 0);
        _telemetryLastFlushAtUtc = DateTime.UtcNow;
    }

    public void RecordTelemetryFlushFailed()
    {
        Interlocked.Increment(ref _telemetryConsecutiveFailures);
        _telemetryLastFailureAtUtc = DateTime.UtcNow;
    }

    public void RecordProjectionMode(bool replicaEnabled)
    {
        _projectionMode = replicaEnabled ? "replicating" : "purging";
    }

    public void RecordProjectionBatchApplied(int appliedCount, long pendingCount, DateTime? oldestPendingAtUtc)
    {
        Interlocked.Add(ref _projectionApplied, appliedCount);
        Interlocked.Exchange(ref _projectionConsecutiveFailures, 0);
        Interlocked.Exchange(ref _projectionLastBatchSize, appliedCount);
        Interlocked.Exchange(ref _projectionPendingCount, pendingCount);
        _projectionOldestPendingAtUtc = oldestPendingAtUtc;
        _projectionLastAppliedAtUtc = DateTime.UtcNow;
    }

    public void RecordProjectionFailure()
    {
        Interlocked.Increment(ref _projectionConsecutiveFailures);
        _projectionLastFailureAtUtc = DateTime.UtcNow;
    }

    public void RecordProjectionPending(long pendingCount, DateTime? oldestPendingAtUtc)
    {
        Interlocked.Exchange(ref _projectionPendingCount, pendingCount);
        _projectionOldestPendingAtUtc = oldestPendingAtUtc;
    }

    public void RecordMaintenanceSucceeded()
    {
        Interlocked.Exchange(ref _maintenanceConsecutiveFailures, 0);
        _maintenanceLastRunAtUtc = DateTime.UtcNow;
    }

    public void RecordMaintenanceFailed()
    {
        Interlocked.Increment(ref _maintenanceConsecutiveFailures);
        _maintenanceLastFailureAtUtc = DateTime.UtcNow;
    }

    public object CreateSnapshot()
    {
        return new
        {
            telemetry = new
            {
                queued = Interlocked.Read(ref _telemetryQueued),
                dropped = Interlocked.Read(ref _telemetryDropped),
                flushed = Interlocked.Read(ref _telemetryFlushed),
                consecutiveFailures = Interlocked.Read(ref _telemetryConsecutiveFailures),
                lastFlushAtUtc = _telemetryLastFlushAtUtc,
                lastFailureAtUtc = _telemetryLastFailureAtUtc
            },
            projection = new
            {
                mode = _projectionMode,
                applied = Interlocked.Read(ref _projectionApplied),
                lastBatchSize = Interlocked.Read(ref _projectionLastBatchSize),
                pendingCount = Interlocked.Read(ref _projectionPendingCount),
                oldestPendingAtUtc = _projectionOldestPendingAtUtc,
                consecutiveFailures = Interlocked.Read(ref _projectionConsecutiveFailures),
                lastAppliedAtUtc = _projectionLastAppliedAtUtc,
                lastFailureAtUtc = _projectionLastFailureAtUtc
            },
            maintenance = new
            {
                consecutiveFailures = Interlocked.Read(ref _maintenanceConsecutiveFailures),
                lastRunAtUtc = _maintenanceLastRunAtUtc,
                lastFailureAtUtc = _maintenanceLastFailureAtUtc
            }
        };
    }

    public bool IsReady(bool projectionReplicaEnabled)
    {
        if (Interlocked.Read(ref _maintenanceConsecutiveFailures) >= 3)
        {
            return false;
        }

        if (Interlocked.Read(ref _telemetryConsecutiveFailures) >= 5)
        {
            return false;
        }

        if (projectionReplicaEnabled)
        {
            if (Interlocked.Read(ref _projectionConsecutiveFailures) >= 3)
            {
                return false;
            }

            if (_projectionOldestPendingAtUtc.HasValue
                && _projectionOldestPendingAtUtc.Value < DateTime.UtcNow.AddMinutes(-5))
            {
                return false;
            }
        }

        return true;
    }
}
