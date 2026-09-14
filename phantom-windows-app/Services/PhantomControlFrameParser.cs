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
        public const int MaxPrefixBytes = 32_768;
        private const int MaxRawPrefixBytes = 65_536;
        private static readonly string[] ThinkOpenTags = ["<thinking>", "<think>"];
        private static readonly string[] ThinkCloseTags = ["</thinking>", "</think>"];

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
            var raw = _prefix.ToString();
            if (Encoding.UTF8.GetByteCount(raw) > MaxRawPrefixBytes)
            {
                throw new PhantomProtocolException("control_prefix_oversized");
            }

            var buffered = Normalize(raw);
            PrefixBytes = Encoding.UTF8.GetByteCount(buffered);
            if (TryCommitFrame(buffered, finalize: false, out var visible))
            {
                return visible;
            }

            if (PrefixBytes > MaxPrefixBytes)
            {
                throw new PhantomProtocolException("control_prefix_oversized");
            }

            return string.Empty;
        }

        public LiveTurnDecision Complete()
        {
            if (Decision is null)
            {
                var buffered = Normalize(_prefix.ToString());
                PrefixBytes = Encoding.UTF8.GetByteCount(buffered);
                _ = TryCommitFrame(buffered, finalize: true, out _);
            }

            var decision = Decision ?? throw new PhantomProtocolException("control_frame_incomplete");
            if (decision.Action == LiveCopilotAction.Retrieve && _bodyHasContent)
                throw new PhantomProtocolException("retrieve_body_not_empty");
            if (decision.Action != LiveCopilotAction.Retrieve && !_bodyHasContent)
                throw new PhantomProtocolException("answer_body_empty");
            return decision;
        }

        public static bool CanFallback(string code) => code is
            "control_frame_incomplete" or "control_prefix_invalid" or "control_prefix_oversized"
            or "control_json_invalid" or "answer_body_empty";

        public static LiveTurnDecision FallbackAnswerDecision() => new(
            LiveCopilotAction.Answer,
            "unknown",
            "general",
            "universal_knowledge",
            "none",
            string.Empty,
            string.Empty,
            Array.Empty<string>(),
            40,
            false,
            0.5);

        public string GetFallbackAnswerText() => ExtractBareAnswer(_prefix.ToString());

        public static string ExtractAnswerBody(string response)
            => TryLocateFrame(Normalize(response), out _, out var rest, finalize: true) ? rest : string.Empty;

        public static string ExtractBareAnswer(string response)
        {
            var text = Normalize(response);
            if (TryLocateFrame(text, out _, out var rest, finalize: true))
            {
                return rest.Trim();
            }

            var protocolIndex = text.IndexOf(ProtocolLine, StringComparison.Ordinal);
            if (protocolIndex >= 0)
            {
                return string.Empty;
            }

            return text.Trim();
        }

        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return StripThink(text.Replace("\r\n", "\n").Replace('\r', '\n'));
        }

        private bool TryCommitFrame(string buffered, bool finalize, out string visible)
        {
            visible = string.Empty;
            if (_bodyStarted) return true;
            if (!TryLocateFrame(buffered, out var json, out var rest, finalize))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new PhantomProtocolException("control_json_invalid");
            }

            Decision = ParseAndValidate(json);
            _bodyStarted = true;
            _prefix.Clear();
            visible = AcceptBody(rest);
            return true;
        }

        private static bool TryLocateFrame(string buffered, out string json, out string rest, bool finalize = false)
        {
            json = string.Empty;
            rest = string.Empty;
            var protocolIndex = buffered.IndexOf(ProtocolLine, StringComparison.Ordinal);
            if (protocolIndex < 0) return false;

            var cursor = protocolIndex + ProtocolLine.Length;
            SkipWhitespace(buffered, ref cursor);
            if (cursor >= buffered.Length || buffered[cursor] != '{')
            {
                return false;
            }

            if (!TryReadJsonObject(buffered, cursor, out var jsonEnd))
            {
                return false;
            }

            json = buffered[cursor..jsonEnd].Trim();
            cursor = jsonEnd;
            SkipWhitespace(buffered, ref cursor);

            const string bodyToken = "PHANTOM_BODY";
            if (cursor < buffered.Length && StartsAt(buffered, cursor, bodyToken))
            {
                cursor += bodyToken.Length;
                if (cursor < buffered.Length && buffered[cursor] == '\n')
                {
                    cursor++;
                }

                rest = buffered[cursor..];
                return true;
            }

            var remaining = cursor < buffered.Length ? buffered[cursor..] : string.Empty;
            if (!finalize && (remaining.Length == 0 || IsTokenPrefix(remaining, bodyToken)))
            {
                return false;
            }

            rest = remaining;
            return true;
        }

        private static void SkipWhitespace(string text, ref int index)
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }

        private static bool StartsAt(string text, int index, string token)
        {
            return index + token.Length <= text.Length
                && string.CompareOrdinal(text, index, token, 0, token.Length) == 0;
        }

        private static bool IsTokenPrefix(string remaining, string token)
            => remaining.Length > 0 && remaining.Length < token.Length
                && token.StartsWith(remaining, StringComparison.Ordinal);

        private static bool TryReadJsonObject(string text, int start, out int endExclusive)
        {
            endExclusive = start;
            if (start >= text.Length || text[start] != '{') return false;

            var depth = 0;
            var inString = false;
            var escape = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (escape)
                    {
                        escape = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escape = true;
                        continue;
                    }

                    if (c == '"') inString = false;
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        endExclusive = i + 1;
                        return true;
                    }
                }
            }

            return false;
        }

        private static string StripThink(string text)
        {
            var value = text;
            while (true)
            {
                var openIndex = IndexOfToken(value, ThinkOpenTags, 0, out var openToken);
                if (openIndex < 0) return value;

                var afterOpen = openIndex + openToken.Length;
                var closeIndex = IndexOfToken(value, ThinkCloseTags, afterOpen, out var closeToken);
                if (closeIndex >= 0)
                {
                    value = value.Remove(openIndex, closeIndex + closeToken.Length - openIndex);
                    continue;
                }

                var protocolIndex = value.IndexOf(ProtocolLine, afterOpen, StringComparison.Ordinal);
                if (protocolIndex >= 0)
                {
                    value = value.Remove(openIndex, protocolIndex - openIndex);
                    continue;
                }

                return value[..openIndex];
            }
        }

        private static int IndexOfToken(string text, string[] tokens, int start, out string token)
        {
            token = string.Empty;
            var best = -1;
            foreach (var candidate in tokens)
            {
                var index = text.IndexOf(candidate, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0) continue;
                if (best < 0 || index < best || (index == best && candidate.Length > token.Length))
                {
                    best = index;
                    token = candidate;
                }
            }

            return best;
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
                "behavioral" => (25, 45),
                "technical" => (20, 40),
                "coding" => (30, 90),
                "system_design" => (40, 75),
                "product_case" => (30, 60),
                "motivation_fit" => (20, 40),
                "personal_factual" => (10, 30),
                "situational" => (20, 40),
                "clarification" or "unknown" => (5, 20),
                "factual_lookup" or "status_update" or "decision_support" or "objection_response"
                    or "risk_tradeoff" or "brainstorm" or "action_capture" => (10, 40),
                _ => (15, 45)
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
