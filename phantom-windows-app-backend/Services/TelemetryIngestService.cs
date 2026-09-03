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
    private static readonly HashSet<string> LiveCopilotFields = new(StringComparer.Ordinal)
    {
        "session_id", "turn_id", "operation_id", "mode", "delivery_style", "stage", "provider", "model",
        "model_call", "attempt", "elapsed_ms", "outcome", "error_code", "question_type", "intent", "action",
        "answer_basis", "confidence_bucket", "entity_type", "has_entity_id", "protocol_version", "prefix_bytes",
        "validation_outcome", "estimated_input_tokens", "max_output_tokens", "recent_turn_count", "snippet_count",
        "image_present", "status_class", "search_mode", "cache_hit", "candidate_count", "transcript_length_bucket",
        "duplicate_suppression_count", "chunk_count", "buffered_characters", "flush_count", "render_ms", "retrieval_status"
    };
    private static readonly string[] SensitiveKeyFragments =
    {
        "question", "transcript", "prompt", "answer", "resume", "snippet", "token", "secret", "authorization",
        "cookie", "email", "phone", "document_id", "entity_id", "path", "image", "clipboard", "connection"
    };

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
            var allowlistedLiveField = string.Equals(request.Category, "live_copilot", StringComparison.Ordinal)
                && LiveCopilotFields.Contains(key);
            if (SensitiveKeyFragments.Any(fragment => key.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                && !allowlistedLiveField
                && !string.Equals(key, "image_present", StringComparison.Ordinal))
            {
                throw new BackendValidationException("Telemetry contains a sensitive attribute key.");
            }
            if (string.Equals(request.Category, "live_copilot", StringComparison.Ordinal)
                && !LiveCopilotFields.Contains(key))
            {
                throw new BackendValidationException("Live Copilot telemetry contains an unallowlisted attribute key.");
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
