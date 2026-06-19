using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class TelemetryIngestService
{
    private readonly TelemetryBufferService _buffer;

    public TelemetryIngestService(TelemetryBufferService buffer)
    {
        _buffer = buffer;
    }

    public bool Ingest(TelemetryIngestRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.EventName))
        {
            throw new BackendValidationException("Category and EventName are required.");
        }

        return _buffer.TryEnqueue(new TelemetryEventRecord
        {
            EventId = $"telemetry-{Guid.NewGuid():N}",
            Category = request.Category,
            EventName = request.EventName,
            PayloadJson = JsonSerializer.Serialize(request.Attributes ?? new Dictionary<string, string>()),
            CreatedAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow
        });
    }
}
