import Foundation

enum LiveCopilotRetrievePolicy {
    private static let personalQuestionTypes: Set<String> = [
        "behavioral", "personal_factual", "motivation_fit", "situational"
    ]
    private static let personalEntityTypes: Set<String> = [
        "profile", "experience", "project", "document"
    ]
    private static let personalBases: Set<String> = [
        "profile_synthesis", "exact_evidence"
    ]
    private static let personalIntents: Set<String> = [
        "candidate_specific", "hybrid"
    ]

    static func shouldForceRetrieve(
        decision: LiveTurnDecision,
        retrievalAvailable: Bool,
        hasActiveEvidence: Bool
    ) -> Bool {
        guard retrievalAvailable, !hasActiveEvidence else { return false }
        guard decision.action == .answer else { return false }
        return looksPersonal(decision)
    }

    static func forceRetrieve(
        _ decision: LiveTurnDecision,
        questionText: String,
        preferredDocumentIds: [String]
    ) -> LiveTurnDecision {
        let query = buildRetrievalQuery(decision: decision, questionText: questionText)
        let docs = !decision.preferredDocumentIds.isEmpty
            ? decision.preferredDocumentIds
            : Array(preferredDocumentIds.prefix(8))
        return LiveTurnDecision(
            action: .retrieve,
            questionType: decision.questionType,
            intent: decision.intent,
            answerBasis: decision.answerBasis,
            entityType: decision.entityType,
            entityId: decision.entityId,
            retrievalQuery: query,
            preferredDocumentIds: docs,
            targetSeconds: decision.targetSeconds,
            allowCode: decision.allowCode,
            confidence: decision.confidence
        )
    }

    static func looksPersonal(_ decision: LiveTurnDecision) -> Bool {
        if personalIntents.contains(decision.intent) { return true }
        if personalEntityTypes.contains(decision.entityType) { return true }
        if personalBases.contains(decision.answerBasis) { return true }
        if personalQuestionTypes.contains(decision.questionType) {
            // Generic advice framed as situational/behavioral should stay universal.
            if decision.intent == "general",
               decision.entityType == "none",
               decision.answerBasis == "universal_knowledge" || decision.answerBasis == "universal_synthesis" {
                return false
            }
            return true
        }
        return false
    }

    private static func buildRetrievalQuery(decision: LiveTurnDecision, questionText: String) -> String {
        let existing = decision.retrievalQuery.trimmingCharacters(in: .whitespacesAndNewlines)
        if !existing.isEmpty { return String(existing.prefix(500)) }
        var parts: [String] = []
        let question = questionText
            .replacingOccurrences(of: "\n", with: " ")
            .replacingOccurrences(of: "\r", with: " ")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        if !question.isEmpty { parts.append(question) }
        if !decision.entityId.isEmpty { parts.append(decision.entityId) }
        let joined = parts.joined(separator: " ").trimmingCharacters(in: .whitespacesAndNewlines)
        if !joined.isEmpty { return String(joined.prefix(500)) }
        return "candidate profile experience project details"
    }
}
