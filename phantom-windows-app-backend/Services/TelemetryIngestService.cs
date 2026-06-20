using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class TelemetryIngestService
{
    private const int MaxAttributes = 32;
    private const int MaxKeyLength = 64;
    private const int MaxValueLength = 512;
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

        if (request.Attributes.Count > MaxAttributes)
        {
            throw new BackendValidationException("Telemetry payload exceeds the maximum attribute count.");
        }

        foreach (var (key, value) in request.Attributes)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > MaxKeyLength)
            {
                throw new BackendValidationException("Telemetry attribute keys must be non-empty and within the allowed length.");
            }

            if ((value?.Length ?? 0) > MaxValueLength)
            {
                throw new BackendValidationException("Telemetry attribute values exceed the maximum allowed length.");
            }
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
