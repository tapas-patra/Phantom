import Foundation

enum InterviewIntent: String, Equatable {
    case candidateSpecific = "Candidate-specific"
    case general = "General"
    case hybrid = "Hybrid"
    case ambiguous = "Ambiguous"
}

enum AnswerSource: String, Equatable {
    case knowledge = "Knowledge"
    case profileBased = "Profile-based"
    case general = "General"
    case clarification = "Clarification"
}

struct AnswerResolution {
    let source: AnswerSource
    let intent: InterviewIntent
    let questionType: String
    let answerBasis: String

    static let initial = AnswerResolution(source: .general, intent: .general, questionType: "unknown", answerBasis: "universal_knowledge")

    init(decision: LiveTurnDecision) {
        source = switch decision.answerBasis {
        case "exact_evidence": .knowledge
        case "profile_synthesis": .profileBased
        case "clarification": .clarification
        default: .general
        }
        intent = switch decision.intent {
        case "candidate_specific": .candidateSpecific
        case "hybrid": .hybrid
        case "ambiguous": .ambiguous
        default: .general
        }
        questionType = decision.questionType
        answerBasis = decision.answerBasis
    }

    private init(source: AnswerSource, intent: InterviewIntent, questionType: String, answerBasis: String) {
        self.source = source
        self.intent = intent
        self.questionType = questionType
        self.answerBasis = answerBasis
    }
}

@MainActor
final class ConversationManager {
    static let recentFullMessageCount = 6

    private struct ModeState {
        var knowledgeRevision = ""
        var activeEvidence: [KnowledgeSnippet] = []
        var activeEntityId = ""
    }

    private var states: [CopilotMode: ModeState] = [.interview: ModeState(), .briefing: ModeState()]
    private(set) var lastTrace = "action=answer calls=0"
    private(set) var lastAnswerResolution = AnswerResolution.initial
    private(set) var lastDecision: LiveTurnDecision?
    private(set) var lastModelCallCount = 0

    func activeEntityId(for mode: CopilotMode) -> String { states[mode]?.activeEntityId ?? "" }
    func activeEvidence(for mode: CopilotMode) -> [KnowledgeSnippet] { states[mode]?.activeEvidence ?? [] }

    func firstCallMessages(
        mode: CopilotMode,
        style: InterviewDeliveryStyle,
        resume: String,
        roleOrMeetingContext: String,
        conversation: [ChatMessage],
        modelId: String,
        knowledgeBase: StartupSnapshot.KnowledgeBase?,
        protocolRepair: Bool = false
    ) -> [ChatMessage] {
        updateRevision(knowledgeBase, mode: mode)
        let system = CopilotPrompt.firstCall(
            mode: mode,
            style: style,
            knowledge: mode == .interview ? knowledgeBase : briefingKnowledge(knowledgeBase),
            resume: mode == .interview ? resume : "",
            roleOrMeetingContext: roleOrMeetingContext,
            activeEvidence: activeEvidence(for: mode),
            protocolRepair: protocolRepair
        )
        return budgeted(system: system, conversation: conversation, modelId: modelId)
    }

    func secondCallMessages(
        mode: CopilotMode,
        style: InterviewDeliveryStyle,
        decision: LiveTurnDecision,
        retrieval: LiveCopilotRetrieval,
        resume: String,
        roleOrMeetingContext: String,
        conversation: [ChatMessage],
        modelId: String,
        knowledgeBase: StartupSnapshot.KnowledgeBase?
    ) -> [ChatMessage] {
        let system = CopilotPrompt.secondCall(
            mode: mode,
            style: style,
            decision: decision,
            retrieval: retrieval,
            knowledge: mode == .interview ? knowledgeBase : briefingKnowledge(knowledgeBase),
            resume: mode == .interview ? resume : "",
            roleOrMeetingContext: roleOrMeetingContext
        )
        return budgeted(system: system, conversation: conversation, modelId: modelId)
    }

    func complete(_ result: LiveCopilotResult, mode: CopilotMode) {
        lastDecision = result.decision
        lastModelCallCount = result.modelCallCount
        lastAnswerResolution = AnswerResolution(decision: result.decision)
        var state = states[mode] ?? ModeState()
        if !result.activeEvidence.isEmpty {
            state.activeEvidence = Array(result.activeEvidence.prefix(3))
            state.activeEntityId = result.decision.entityId
        }
        states[mode] = state
        lastTrace = "action=\(result.decision.action.rawValue) type=\(result.decision.questionType) basis=\(result.decision.answerBasis) calls=\(result.modelCallCount) retrieval=\(result.retrievalStatus)"
    }

    func warm(_ knowledgeBase: StartupSnapshot.KnowledgeBase) {
        updateRevision(knowledgeBase, mode: .interview)
    }

    func reset(mode: CopilotMode? = nil) {
        if let mode { states[mode] = ModeState() }
        else { states = [.interview: ModeState(), .briefing: ModeState()] }
        lastTrace = "action=answer calls=0"
        lastAnswerResolution = .initial
        lastDecision = nil
        lastModelCallCount = 0
    }

    static func estimate(_ text: String) -> Int { max(1, text.count / 4) }

    func finalizeAssistantResponse(_ response: String) -> (content: String, summary: String) {
        let pattern = #"SUMMARY:\s*(.+?)(?:\n|$)"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
              let match = regex.firstMatch(in: response, range: NSRange(response.startIndex..., in: response)),
              let summaryRange = Range(match.range(at: 1), in: response) else {
            return (response, response.count > 100 ? String(response.prefix(100)) + "..." : response)
        }
        let summary = String(response[summaryRange]).trimmingCharacters(in: .whitespacesAndNewlines)
        let content = regex.stringByReplacingMatches(in: response, range: NSRange(response.startIndex..., in: response), withTemplate: "")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        return (content, summary)
    }

    private func updateRevision(_ knowledgeBase: StartupSnapshot.KnowledgeBase?, mode: CopilotMode) {
        guard let knowledgeBase else { return }
        let revision = "\(knowledgeBase.embeddingVersion):\(knowledgeBase.documentCount):\(knowledgeBase.chunkCount)"
        var state = states[mode] ?? ModeState()
        if state.knowledgeRevision != revision {
            state.knowledgeRevision = revision
            state.activeEvidence = []
            state.activeEntityId = ""
            states[mode] = state
        }
    }

    private func briefingKnowledge(_ knowledgeBase: StartupSnapshot.KnowledgeBase?) -> StartupSnapshot.KnowledgeBase? {
        guard let knowledgeBase else { return nil }
        return StartupSnapshot.KnowledgeBase(
            knowledgeBaseId: knowledgeBase.knowledgeBaseId,
            name: knowledgeBase.name,
            description: knowledgeBase.description,
            status: knowledgeBase.status,
            embeddingModel: knowledgeBase.embeddingModel,
            embeddingVersion: knowledgeBase.embeddingVersion,
            documentCount: knowledgeBase.documentCount,
            chunkCount: knowledgeBase.chunkCount,
            canUseInInterview: knowledgeBase.canUseInInterview,
            blockedReason: knowledgeBase.blockedReason,
            lastProcessedAtUtc: knowledgeBase.lastProcessedAtUtc,
            profileCard: nil,
            experienceCards: [],
            projectCards: knowledgeBase.projectCards
        )
    }

    private func budgeted(system: String, conversation: [ChatMessage], modelId: String) -> [ChatMessage] {
        let configuration = ModelContextRegistry.configuration(for: modelId)
        var budget = max(500, configuration.maxContextTokens - configuration.maxResponseTokens - Self.estimate(system))
        let valid = conversation.filter { !$0.content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        var recent: [ChatMessage] = []
        for message in valid.reversed() {
            guard recent.count < Self.recentFullMessageCount else { break }
            let content = String(message.content.prefix(2_400))
            let tokens = Self.estimate(content)
            if !recent.isEmpty, budget - tokens < 100 { break }
            recent.append(ChatMessage(id: message.id, role: message.role, content: content, summary: message.summary))
            budget -= tokens
        }
        return [ChatMessage(role: "system", content: system)] + Array(recent.reversed())
    }
}
