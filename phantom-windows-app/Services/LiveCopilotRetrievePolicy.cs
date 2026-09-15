using System;
using System.Collections.Generic;
using System.Linq;
using SecureOverlay.Domain;

namespace SecureOverlay.Services
{
    public static class LiveCopilotRetrievePolicy
    {
        private static readonly HashSet<string> PersonalQuestionTypes = new(StringComparer.Ordinal)
        {
            "behavioral", "personal_factual", "motivation_fit", "situational"
        };

        private static readonly HashSet<string> PersonalEntityTypes = new(StringComparer.Ordinal)
        {
            "profile", "experience", "project", "document"
        };

        private static readonly HashSet<string> PersonalBases = new(StringComparer.Ordinal)
        {
            "profile_synthesis", "exact_evidence"
        };

        private static readonly HashSet<string> PersonalIntents = new(StringComparer.Ordinal)
        {
            "candidate_specific", "hybrid"
        };

        public static bool ShouldForceRetrieve(
            LiveTurnDecision decision,
            bool retrievalAvailable,
            bool hasActiveEvidence)
        {
            _ = decision;
            _ = retrievalAvailable;
            _ = hasActiveEvidence;
            return false;
        }

        public static LiveTurnDecision PrepareRetrieve(
            LiveTurnDecision decision,
            string questionText,
            IReadOnlyList<string>? preferredDocumentIds)
        {
            var docs = decision.PreferredDocumentIds.Count > 0
                ? decision.PreferredDocumentIds
                : (preferredDocumentIds ?? Array.Empty<string>()).Take(8).ToArray();
            return decision with
            {
                Action = LiveCopilotAction.Retrieve,
                RetrievalQuery = NormalizeRetrievalQuery(decision, questionText),
                PreferredDocumentIds = docs
            };
        }

        public static bool CanReuseSpeculative(
            string speculativeQuery,
            IReadOnlyList<string>? speculativeDocuments,
            string query,
            IReadOnlyList<string>? documents,
            string status)
        {
            if (status is not ("found" or "empty")) return false;
            if (!QueriesAreSimilar(speculativeQuery, query)) return false;
            return DocumentSetsEqual(speculativeDocuments, documents);
        }

        public static bool DocumentSetsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
        {
            var a = NormalizeDocumentSet(left);
            var b = NormalizeDocumentSet(right);
            return a.SetEquals(b);
        }

        public static LiveTurnDecision ForceRetrieve(
            LiveTurnDecision decision,
            string questionText,
            IReadOnlyList<string> preferredDocumentIds)
            => PrepareRetrieve(decision, questionText, preferredDocumentIds);

        public static string NormalizeRetrievalQuery(LiveTurnDecision decision, string questionText)
        {
            if (!string.IsNullOrWhiteSpace(decision.RetrievalQuery))
                return TrimQuery(decision.RetrievalQuery);

            var parts = new List<string>();
            var question = (questionText ?? string.Empty)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Trim();
            if (!string.IsNullOrWhiteSpace(question)) parts.Add(question);
            if (!string.IsNullOrWhiteSpace(decision.EntityId)) parts.Add(decision.EntityId);
            var joined = string.Join(' ', parts).Trim();
            return string.IsNullOrWhiteSpace(joined)
                ? "candidate profile experience project details"
                : TrimQuery(joined);
        }

        public static bool QueriesAreSimilar(string left, string right)
        {
            var a = NormalizeForCompare(left);
            var b = NormalizeForCompare(right);
            if (a.Length == 0 || b.Length == 0) return false;
            if (string.Equals(a, b, StringComparison.Ordinal)) return true;
            return a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal);
        }

        public static bool LooksPersonal(LiveTurnDecision decision)
        {
            if (PersonalIntents.Contains(decision.Intent)) return true;
            if (PersonalEntityTypes.Contains(decision.EntityType)) return true;
            if (PersonalBases.Contains(decision.AnswerBasis)) return true;
            if (PersonalQuestionTypes.Contains(decision.QuestionType))
            {
                // Generic advice framed as situational/behavioral should stay universal.
                if (string.Equals(decision.Intent, "general", StringComparison.Ordinal) &&
                    string.Equals(decision.EntityType, "none", StringComparison.Ordinal) &&
                    (string.Equals(decision.AnswerBasis, "universal_knowledge", StringComparison.Ordinal) ||
                     string.Equals(decision.AnswerBasis, "universal_synthesis", StringComparison.Ordinal)))
                {
                    return false;
                }

                return true;
            }

            return false;
        }

        private static string NormalizeForCompare(string value)
            => string.Join(' ', (value ?? string.Empty)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));

        private static string TrimQuery(string value)
        {
            var normalized = value.Trim();
            return normalized.Length <= 500 ? normalized : normalized[..500];
        }

        private static HashSet<string> NormalizeDocumentSet(IReadOnlyList<string>? value)
            => new(
                (value ?? Array.Empty<string>()).Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
    }
}
