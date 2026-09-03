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
    private readonly ILogger<TelemetryIngestService> _logger;
    private static readonly HashSet<string> LiveCopilotFields = new(StringComparer.Ordinal)
    {
        "session_id", "turn_id", "operation_id", "mode", "delivery_style", "stage", "provider", "model",
        "model_call", "attempt", "elapsed_ms", "outcome", "error_code", "question_type", "intent", "action",
        "answer_basis", "confidence_bucket", "entity_type", "has_entity_id", "protocol_version", "prefix_bytes",
        "validation_outcome", "estimated_input_tokens", "max_output_tokens", "recent_turn_count", "snippet_count",
        "image_present", "status_class", "search_mode", "cache_hit", "candidate_count", "transcript_length_bucket",
        "duplicate_suppression_count", "chunk_count", "buffered_characters", "flush_count", "render_ms", "retrieval_status", "finish_reason",
        "input_type", "model_call_count"
    };
    private static readonly string[] SensitiveKeyFragments =
    {
        "question", "transcript", "prompt", "answer", "resume", "snippet", "token", "secret", "authorization",
        "cookie", "email", "phone", "document_id", "entity_id", "path", "image", "clipboard", "connection"
    };

    public TelemetryIngestService(TelemetryBufferService buffer, ILogger<TelemetryIngestService> logger)
    {
        _buffer = buffer;
        _logger = logger;
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

        var accepted = _buffer.TryEnqueue(new TelemetryEventRecord
        {
            EventId = $"telemetry-{Guid.NewGuid():N}",
            Category = request.Category,
            EventName = request.EventName,
            PayloadJson = JsonSerializer.Serialize(request.Attributes ?? new Dictionary<string, string>()),
            CreatedAtUtc = request.OccurredAtUtc ?? DateTime.UtcNow
        });
        if (accepted && string.Equals(request.Category, "live_copilot", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "desktop_telemetry service={Service} component={Component} category={Category} event={Event} session_id={SessionId} turn_id={TurnId} operation_id={OperationId} mode={Mode} delivery_style={DeliveryStyle} stage={Stage} provider={Provider} model={Model} model_call={ModelCall} attempt={Attempt} outcome={Outcome} error_code={ErrorCode} elapsed_ms={ElapsedMs} question_type={QuestionType} action={Action} answer_basis={AnswerBasis} retrieval_status={RetrievalStatus} snippet_count={SnippetCount} chunk_count={ChunkCount} buffered_characters={BufferedCharacters} flush_count={FlushCount} finish_reason={FinishReason}",
                "phantom-windows-app-backend", "desktop_telemetry",
                request.Category, Safe(request.EventName),
                Value("session_id"), Value("turn_id"), Value("operation_id"), Value("mode"), Value("delivery_style"), Value("stage"),
                Value("provider"), Value("model"), Value("model_call"), Value("attempt"), Value("outcome"), Value("error_code"), Value("elapsed_ms"),
                Value("question_type"), Value("action"), Value("answer_basis"), Value("retrieval_status"), Value("snippet_count"),
                Value("chunk_count"), Value("buffered_characters"), Value("flush_count"), Value("finish_reason"));
        }
        return accepted;

        string Value(string key) => request.Attributes is { } attributes && attributes.TryGetValue(key, out var value)
            ? Safe(value)
            : string.Empty;
        static string Safe(string value) => value.Length <= 160
            && value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or ':')
                ? value
                : string.Empty;
    }
}
