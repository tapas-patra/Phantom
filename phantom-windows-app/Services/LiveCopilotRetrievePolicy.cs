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
            if (!retrievalAvailable || hasActiveEvidence) return false;
            if (decision.Action != LiveCopilotAction.Answer) return false;
            return LooksPersonal(decision);
        }

        public static LiveTurnDecision ForceRetrieve(
            LiveTurnDecision decision,
            string questionText,
            IReadOnlyList<string> preferredDocumentIds)
        {
            var query = BuildRetrievalQuery(decision, questionText);
            var docs = decision.PreferredDocumentIds.Count > 0
                ? decision.PreferredDocumentIds
                : (preferredDocumentIds ?? Array.Empty<string>()).Take(8).ToArray();
            return decision with
            {
                Action = LiveCopilotAction.Retrieve,
                RetrievalQuery = query,
                PreferredDocumentIds = docs
            };
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

        private static string BuildRetrievalQuery(LiveTurnDecision decision, string questionText)
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

        private static string TrimQuery(string value)
        {
            var normalized = value.Trim();
            return normalized.Length <= 500 ? normalized : normalized[..500];
        }
    }
}
