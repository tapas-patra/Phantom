using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SecureOverlay.Domain;

namespace SecureOverlay.Services
{
    public sealed class PhantomControlFrameParser
    {
        public const string ProtocolLine = "PHANTOM_CONTROL_V1";
        public const string BodyDelimiter = "\nPHANTOM_BODY\n";
        public const int MaxPrefixBytes = 4096;

        private static readonly HashSet<string> Actions = new(StringComparer.Ordinal) { "answer", "retrieve", "clarify" };
        private static readonly HashSet<string> QuestionTypes = new(StringComparer.Ordinal)
        {
            "behavioral", "technical", "coding", "system_design", "product_case", "motivation_fit",
            "personal_factual", "situational", "clarification", "unknown", "factual_lookup", "status_update",
            "decision_support", "objection_response", "risk_tradeoff", "brainstorm", "action_capture"
        };
        private static readonly HashSet<string> Intents = new(StringComparer.Ordinal) { "candidate_specific", "general", "hybrid", "ambiguous" };
        private static readonly HashSet<string> Bases = new(StringComparer.Ordinal) { "exact_evidence", "profile_synthesis", "universal_knowledge", "universal_synthesis", "clarification" };
        private static readonly HashSet<string> EntityTypes = new(StringComparer.Ordinal) { "none", "profile", "experience", "project", "document", "context_pack", "task" };

        private readonly HashSet<string> _allowedEntityIds;
        private readonly HashSet<string> _allowedDocumentIds;
        private readonly StringBuilder _prefix = new();
        private bool _bodyStarted;
        private bool _bodyHasContent;
        private int _receivedCharacters;

        public PhantomControlFrameParser(IEnumerable<string>? allowedEntityIds = null, IEnumerable<string>? allowedDocumentIds = null)
        {
            _allowedEntityIds = new HashSet<string>(allowedEntityIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            _allowedDocumentIds = new HashSet<string>(allowedDocumentIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        }

        public LiveTurnDecision? Decision { get; private set; }
        public int PrefixBytes { get; private set; }
        public bool HasReceivedChunks => _receivedCharacters > 0;

        public string Feed(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk)) return string.Empty;
            _receivedCharacters += chunk.Length;
            if (_bodyStarted) return AcceptBody(chunk);

            _prefix.Append(chunk);
            PrefixBytes = Encoding.UTF8.GetByteCount(_prefix.ToString());
            if (PrefixBytes > MaxPrefixBytes)
            {
                throw new PhantomProtocolException("control_prefix_oversized");
            }

            var buffered = _prefix.ToString();
            var delimiterIndex = buffered.IndexOf(BodyDelimiter, StringComparison.Ordinal);
            if (delimiterIndex < 0) return string.Empty;

            var header = buffered[..delimiterIndex];
            var newline = header.IndexOf('\n');
            if (newline <= 0 || !string.Equals(header[..newline], ProtocolLine, StringComparison.Ordinal))
            {
                throw new PhantomProtocolException("control_prefix_invalid");
            }

            var json = header[(newline + 1)..];
            if (json.Contains('\n') || string.IsNullOrWhiteSpace(json))
            {
                throw new PhantomProtocolException("control_json_invalid");
            }

            Decision = ParseAndValidate(json);
            _bodyStarted = true;
            _prefix.Clear();
            return AcceptBody(buffered[(delimiterIndex + BodyDelimiter.Length)..]);
        }

        public LiveTurnDecision Complete()
        {
            var decision = Decision ?? throw new PhantomProtocolException("control_frame_incomplete");
            if (decision.Action == LiveCopilotAction.Retrieve && _bodyHasContent)
                throw new PhantomProtocolException("retrieve_body_not_empty");
            if (decision.Action != LiveCopilotAction.Retrieve && !_bodyHasContent)
                throw new PhantomProtocolException("answer_body_empty");
            return decision;
        }

        private string AcceptBody(string body)
        {
            if (!string.IsNullOrWhiteSpace(body)) _bodyHasContent = true;
            return Decision?.Action == LiveCopilotAction.Retrieve ? string.Empty : body;
        }

        private LiveTurnDecision ParseAndValidate(string json)
        {
            ControlFrameDto dto;
            try
            {
                dto = JsonSerializer.Deserialize<ControlFrameDto>(json, JsonOptions)
                    ?? throw new PhantomProtocolException("control_json_invalid");
            }
            catch (JsonException)
            {
                throw new PhantomProtocolException("control_json_invalid");
            }

            if (!Actions.Contains(dto.Action) || !QuestionTypes.Contains(dto.QuestionType)
                || !Intents.Contains(dto.Intent) || !Bases.Contains(dto.AnswerBasis)
                || !EntityTypes.Contains(dto.EntityType))
                throw new PhantomProtocolException("control_enum_invalid");
            if (dto.Confidence is < 0 or > 1 || dto.EntityId.Length > 160 || dto.RetrievalQuery.Length > 500)
                throw new PhantomProtocolException("control_value_out_of_range");
            if (dto.PreferredDocumentIds.Count > 8 || dto.PreferredDocumentIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 160))
                throw new PhantomProtocolException("control_document_limit");
            if (dto.PreferredDocumentIds.Distinct(StringComparer.Ordinal).Count() != dto.PreferredDocumentIds.Count)
                throw new PhantomProtocolException("control_document_duplicate");
            if (!string.IsNullOrEmpty(dto.EntityId) && !_allowedEntityIds.Contains(dto.EntityId))
                throw new PhantomProtocolException("control_entity_unknown");
            if (dto.PreferredDocumentIds.Any(id => !_allowedDocumentIds.Contains(id)))
                throw new PhantomProtocolException("control_document_unknown");

            var action = dto.Action switch
            {
                "answer" => LiveCopilotAction.Answer,
                "retrieve" => LiveCopilotAction.Retrieve,
                _ => LiveCopilotAction.Clarify
            };
            if (action == LiveCopilotAction.Retrieve && string.IsNullOrWhiteSpace(dto.RetrievalQuery))
                throw new PhantomProtocolException("retrieval_query_empty");
            if (action != LiveCopilotAction.Retrieve && !string.IsNullOrWhiteSpace(dto.RetrievalQuery))
                throw new PhantomProtocolException("unexpected_retrieval_query");
            if (action == LiveCopilotAction.Clarify && dto.AnswerBasis != "clarification")
                throw new PhantomProtocolException("clarification_basis_invalid");

            return new LiveTurnDecision(
                action,
                dto.QuestionType,
                dto.Intent,
                dto.AnswerBasis,
                dto.EntityType,
                dto.EntityId,
                dto.RetrievalQuery.Trim(),
                dto.PreferredDocumentIds,
                ClampTargetSeconds(dto.QuestionType, dto.TargetSeconds),
                dto.AllowCode,
                dto.Confidence);
        }

        private static int ClampTargetSeconds(string questionType, int value)
        {
            var (minimum, maximum) = questionType switch
            {
                "behavioral" => (45, 75),
                "technical" => (30, 60),
                "coding" => (45, 180),
                "system_design" => (60, 180),
                "product_case" => (45, 120),
                "motivation_fit" => (30, 60),
                "personal_factual" => (15, 60),
                "clarification" or "unknown" => (5, 20),
                _ => (15, 120)
            };
            return Math.Clamp(value, minimum, maximum);
        }

        private sealed class ControlFrameDto
        {
            public string Action { get; init; } = "";
            public string QuestionType { get; init; } = "";
            public string Intent { get; init; } = "";
            public string AnswerBasis { get; init; } = "";
            public string EntityType { get; init; } = "";
            public string EntityId { get; init; } = "";
            public string RetrievalQuery { get; init; } = "";
            public List<string> PreferredDocumentIds { get; init; } = new();
            public int TargetSeconds { get; init; }
            public bool AllowCode { get; init; }
            public double Confidence { get; init; }
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
    }
}
