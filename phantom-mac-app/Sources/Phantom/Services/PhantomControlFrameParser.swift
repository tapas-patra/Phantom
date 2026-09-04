import Foundation

enum CopilotMode: String, Codable, CaseIterable, Hashable {
    case interview
    case briefing
}

enum InterviewDeliveryStyle: String, Codable, CaseIterable, Hashable {
    case standard
    case desi
}

enum LiveCopilotAction: String, Codable {
    case answer
    case retrieve
    case clarify
}

struct LiveTurnDecision: Equatable {
    let action: LiveCopilotAction
    let questionType: String
    let intent: String
    let answerBasis: String
    let entityType: String
    let entityId: String
    let retrievalQuery: String
    let preferredDocumentIds: [String]
    let targetSeconds: Int
    let allowCode: Bool
    let confidence: Double
    let protocolVersion = 1
}

struct LiveCopilotRetrieval {
    let status: String
    let snippets: [KnowledgeSnippet]
    let kbRevision: String
}

struct LiveCopilotResult {
    let answer: String
    let decision: LiveTurnDecision
    let modelCallCount: Int
    let protocolRetryCount: Int
    let retrievalStatus: String
    let activeEvidence: [KnowledgeSnippet]
}

struct PhantomProtocolError: LocalizedError {
    let code: String
    var errorDescription: String? { "The selected model returned an invalid live-response header." }
}

final class PhantomControlFrameParser {
    static let protocolLine = "PHANTOM_CONTROL_V1"
    static let bodyDelimiter = "\nPHANTOM_BODY\n"
    static let maxPrefixBytes = 4_096

    private static let actions = Set(["answer", "retrieve", "clarify"])
    private static let questionTypes = Set([
        "behavioral", "technical", "coding", "system_design", "product_case", "motivation_fit",
        "personal_factual", "situational", "clarification", "unknown", "factual_lookup", "status_update",
        "decision_support", "objection_response", "risk_tradeoff", "brainstorm", "action_capture"
    ])
    private static let intents = Set(["candidate_specific", "general", "hybrid", "ambiguous"])
    private static let bases = Set(["exact_evidence", "profile_synthesis", "universal_knowledge", "universal_synthesis", "clarification"])
    private static let entityTypes = Set(["none", "profile", "experience", "project", "document", "context_pack", "task"])

    private let allowedEntityIds: Set<String>
    private let allowedDocumentIds: Set<String>
    private var prefix = ""
    private var bodyStarted = false
    private var bodyHasContent = false
    private(set) var decision: LiveTurnDecision?
    private(set) var prefixBytes = 0
    private(set) var hasReceivedChunks = false

    init(allowedEntityIds: [String] = [], allowedDocumentIds: [String] = []) {
        self.allowedEntityIds = Set(allowedEntityIds)
        self.allowedDocumentIds = Set(allowedDocumentIds)
    }

    func feed(_ chunk: String) throws -> String {
        guard !chunk.isEmpty else { return "" }
        hasReceivedChunks = true
        if bodyStarted { return acceptBody(chunk) }
        prefix += chunk
        prefixBytes = prefix.lengthOfBytes(using: .utf8)
        guard prefixBytes <= Self.maxPrefixBytes else { throw PhantomProtocolError(code: "control_prefix_oversized") }
        guard let range = prefix.range(of: Self.bodyDelimiter) else { return "" }
        let header = String(prefix[..<range.lowerBound])
        guard let newline = header.firstIndex(of: "\n"),
              String(header[..<newline]) == Self.protocolLine else {
            throw PhantomProtocolError(code: "control_prefix_invalid")
        }
        let json = String(header[header.index(after: newline)...])
        guard !json.isEmpty, !json.contains("\n") else { throw PhantomProtocolError(code: "control_json_invalid") }
        decision = try Self.parse(json, allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
        bodyStarted = true
        let body = String(prefix[range.upperBound...])
        prefix = ""
        return acceptBody(body)
    }

    func complete() throws -> LiveTurnDecision {
        guard let decision else { throw PhantomProtocolError(code: "control_frame_incomplete") }
        if decision.action == .retrieve, bodyHasContent { throw PhantomProtocolError(code: "retrieve_body_not_empty") }
        if decision.action != .retrieve, !bodyHasContent { throw PhantomProtocolError(code: "answer_body_empty") }
        return decision
    }

    private func acceptBody(_ body: String) -> String {
        if !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { bodyHasContent = true }
        return decision?.action == .retrieve ? "" : body
    }

    private static func parse(_ json: String, allowedEntityIds: Set<String>, allowedDocumentIds: Set<String>) throws -> LiveTurnDecision {
        let dto: ControlFrame
        let data = Data(json.utf8)
        do {
            guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  Set(object.keys) == ControlFrame.keys else {
                throw PhantomProtocolError(code: "control_json_invalid")
            }
            dto = try JSONDecoder().decode(ControlFrame.self, from: data)
        }
        catch { throw PhantomProtocolError(code: "control_json_invalid") }
        guard actions.contains(dto.action), questionTypes.contains(dto.questionType), intents.contains(dto.intent),
              bases.contains(dto.answerBasis), entityTypes.contains(dto.entityType) else {
            throw PhantomProtocolError(code: "control_enum_invalid")
        }
        guard (0...1).contains(dto.confidence), dto.entityId.count <= 160, dto.retrievalQuery.count <= 500 else {
            throw PhantomProtocolError(code: "control_value_out_of_range")
        }
        guard dto.preferredDocumentIds.count <= 8,
              Set(dto.preferredDocumentIds).count == dto.preferredDocumentIds.count,
              dto.preferredDocumentIds.allSatisfy({ !$0.isEmpty && $0.count <= 160 }) else {
            throw PhantomProtocolError(code: "control_document_limit")
        }
        guard dto.entityId.isEmpty || allowedEntityIds.contains(dto.entityId) else { throw PhantomProtocolError(code: "control_entity_unknown") }
        guard dto.preferredDocumentIds.allSatisfy(allowedDocumentIds.contains) else { throw PhantomProtocolError(code: "control_document_unknown") }
        guard let action = LiveCopilotAction(rawValue: dto.action) else { throw PhantomProtocolError(code: "control_enum_invalid") }
        guard action != .retrieve || !dto.retrievalQuery.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw PhantomProtocolError(code: "retrieval_query_empty")
        }
        guard action == .retrieve || dto.retrievalQuery.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw PhantomProtocolError(code: "unexpected_retrieval_query")
        }
        guard action != .clarify || dto.answerBasis == "clarification" else { throw PhantomProtocolError(code: "clarification_basis_invalid") }
        return LiveTurnDecision(
            action: action, questionType: dto.questionType, intent: dto.intent, answerBasis: dto.answerBasis,
            entityType: dto.entityType, entityId: dto.entityId,
            retrievalQuery: dto.retrievalQuery.trimmingCharacters(in: .whitespacesAndNewlines),
            preferredDocumentIds: dto.preferredDocumentIds,
            targetSeconds: clampTargetSeconds(dto.targetSeconds, questionType: dto.questionType),
            allowCode: dto.allowCode, confidence: dto.confidence
        )
    }

    private static func clampTargetSeconds(_ value: Int, questionType: String) -> Int {
        let bounds: ClosedRange<Int>
        switch questionType {
        case "behavioral": bounds = 45...75
        case "technical": bounds = 30...60
        case "coding", "system_design": bounds = 45...180
        case "product_case": bounds = 45...120
        case "motivation_fit": bounds = 30...60
        case "personal_factual": bounds = 15...60
        case "clarification", "unknown": bounds = 5...20
        default: bounds = 15...120
        }
        return min(max(value, bounds.lowerBound), bounds.upperBound)
    }

    private struct ControlFrame: Decodable {
        static let keys = Set([
            "action", "questionType", "intent", "answerBasis", "entityType", "entityId",
            "retrievalQuery", "preferredDocumentIds", "targetSeconds", "allowCode", "confidence"
        ])
        let action: String
        let questionType: String
        let intent: String
        let answerBasis: String
        let entityType: String
        let entityId: String
        let retrievalQuery: String
        let preferredDocumentIds: [String]
        let targetSeconds: Int
        let allowCode: Bool
        let confidence: Double
    }
}
