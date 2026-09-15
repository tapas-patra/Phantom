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

    func withRetrieval(query: String, preferredDocumentIds: [String]) -> LiveTurnDecision {
        LiveTurnDecision(
            action: .retrieve,
            questionType: questionType,
            intent: intent,
            answerBasis: answerBasis,
            entityType: entityType,
            entityId: entityId,
            retrievalQuery: query,
            preferredDocumentIds: preferredDocumentIds,
            targetSeconds: targetSeconds,
            allowCode: allowCode,
            confidence: confidence
        )
    }
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
    static let maxPrefixBytes = 32_768
    private static let maxRawPrefixBytes = 65_536
    private static let thinkOpenTags = ["<thinking>", "<think>"]
    private static let thinkCloseTags = ["</thinking>", "</think>"]

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
        let rawBytes = prefix.lengthOfBytes(using: .utf8)
        guard rawBytes <= Self.maxRawPrefixBytes else { throw PhantomProtocolError(code: "control_prefix_oversized") }
        let buffered = Self.normalize(prefix)
        prefixBytes = buffered.lengthOfBytes(using: .utf8)
        if let visible = try commitFrame(buffered, finalize: false) {
            return visible
        }
        guard prefixBytes <= Self.maxPrefixBytes else { throw PhantomProtocolError(code: "control_prefix_oversized") }
        return ""
    }

    func complete() throws -> LiveTurnDecision {
        if decision == nil {
            let buffered = Self.normalize(prefix)
            prefixBytes = buffered.lengthOfBytes(using: .utf8)
            _ = try commitFrame(buffered, finalize: true)
        }
        guard let decision else { throw PhantomProtocolError(code: "control_frame_incomplete") }
        if decision.action != .retrieve, !bodyHasContent { throw PhantomProtocolError(code: "answer_body_empty") }
        return decision
    }

    static func canFallback(_ code: String) -> Bool {
        [
            "control_frame_incomplete", "control_prefix_invalid", "control_prefix_oversized",
            "control_json_invalid", "answer_body_empty", "control_enum_invalid",
            "control_value_out_of_range", "control_document_limit", "control_document_duplicate",
            "control_entity_unknown", "control_document_unknown", "retrieval_query_empty",
            "unexpected_retrieval_query", "clarification_basis_invalid", "retrieve_body_not_empty"
        ].contains(code)
    }

    static func fallbackAnswerDecision() -> LiveTurnDecision {
        LiveTurnDecision(
            action: .answer, questionType: "unknown", intent: "general",
            answerBasis: "universal_knowledge", entityType: "none", entityId: "",
            retrievalQuery: "", preferredDocumentIds: [], targetSeconds: 40,
            allowCode: false, confidence: 0.5
        )
    }

    func fallbackAnswerText() -> String { Self.extractBareAnswer(prefix) }

    static func extractAnswerBody(_ response: String) -> String {
        let text = normalize(response)
        return locateFrame(text, finalize: true)?.rest ?? ""
    }

    static func resolveAnswer(_ response: String) -> String {
        let body = extractAnswerBody(response)
        if !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { return body }
        return extractBareAnswer(response)
    }

    static func extractBareAnswer(_ response: String) -> String {
        let text = normalize(response)
        if let frame = locateFrame(text, finalize: true) { return frame.rest.trimmingCharacters(in: .whitespacesAndNewlines) }
        if text.contains(protocolLine) { return "" }
        return text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    static func normalize(_ text: String) -> String {
        guard !text.isEmpty else { return "" }
        return stripThink(text.replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n"))
    }

    private func commitFrame(_ buffered: String, finalize: Bool) throws -> String? {
        if bodyStarted { return "" }
        if let frame = Self.locateFrame(buffered, finalize: finalize) {
            guard !frame.json.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                throw PhantomProtocolError(code: "control_json_invalid")
            }
            decision = try Self.parse(frame.json, allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
            bodyStarted = true
            prefix = ""
            return acceptBody(frame.rest)
        }
        guard Self.looksLikeBareAnswer(buffered, finalize: finalize) else { return nil }
        decision = Self.fallbackAnswerDecision()
        bodyStarted = true
        prefix = ""
        return acceptBody(buffered)
    }

    private static func locateFrame(_ buffered: String, finalize: Bool = false) -> (json: String, rest: String)? {
        let protocolRange = buffered.range(of: protocolLine)
        var cursor = buffered.startIndex
        if let protocolRange {
            cursor = protocolRange.upperBound
        }
        skipControlNoise(buffered, from: &cursor)
        guard cursor < buffered.endIndex, buffered[cursor] == "{" else { return nil }
        guard let jsonEnd = readJsonObjectEnd(buffered, from: cursor) else { return nil }
        let json = String(buffered[cursor..<jsonEnd]).trimmingCharacters(in: .whitespacesAndNewlines)
        if protocolRange == nil, !json.contains("\"action\"") {
            return nil
        }
        cursor = jsonEnd
        skipControlNoise(buffered, from: &cursor)

        let bodyToken = "PHANTOM_BODY"
        if cursor < buffered.endIndex, buffered[cursor...].hasPrefix(bodyToken) {
            cursor = buffered.index(cursor, offsetBy: bodyToken.count)
            if cursor < buffered.endIndex, buffered[cursor] == "\n" {
                cursor = buffered.index(after: cursor)
            }
            return (json, String(buffered[cursor...]))
        }

        let remaining = cursor < buffered.endIndex ? String(buffered[cursor...]) : ""
        if !finalize && (remaining.isEmpty || isTokenPrefix(remaining, of: bodyToken)) {
            return nil
        }
        return (json, remaining)
    }

    private static func looksLikeBareAnswer(_ buffered: String, finalize: Bool) -> Bool {
        let text = buffered.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return false }
        if text.contains(protocolLine) { return false }
        let leading = buffered.drop(while: { $0.isWhitespace })
        let leadingText = String(leading)
        if isTokenPrefix(leadingText, of: protocolLine) { return false }
        guard let first = leadingText.first else { return false }
        if first == "{" { return false }
        if leadingText.hasPrefix("```") { return finalize && leadingText.count >= 80 }
        return finalize || leadingText.count >= 20
    }

    private static func skipControlNoise(_ text: String, from index: inout String.Index) {
        while index < text.endIndex {
            skipWhitespace(text, from: &index)
            guard index < text.endIndex, text[index...].hasPrefix("```") else { break }
            index = text.index(index, offsetBy: 3)
            while index < text.endIndex, text[index] != "\n", text[index] != "{" {
                index = text.index(after: index)
            }
            if index < text.endIndex, text[index] == "\n" {
                index = text.index(after: index)
            }
        }
    }

    private static func skipWhitespace(_ text: String, from index: inout String.Index) {
        while index < text.endIndex, text[index].isWhitespace {
            index = text.index(after: index)
        }
    }

    private static func isTokenPrefix(_ remaining: String, of token: String) -> Bool {
        !remaining.isEmpty && remaining.count < token.count && token.hasPrefix(remaining)
    }

    private static func readJsonObjectEnd(_ text: String, from start: String.Index) -> String.Index? {
        guard start < text.endIndex, text[start] == "{" else { return nil }
        var depth = 0
        var inString = false
        var escape = false
        var index = start
        while index < text.endIndex {
            let character = text[index]
            let next = text.index(after: index)
            if inString {
                if escape {
                    escape = false
                } else if character == "\\" {
                    escape = true
                } else if character == "\"" {
                    inString = false
                }
                index = next
                continue
            }
            if character == "\"" {
                inString = true
            } else if character == "{" {
                depth += 1
            } else if character == "}" {
                depth -= 1
                if depth == 0 {
                    return next
                }
            }
            index = next
        }
        return nil
    }

    private static func stripThink(_ text: String) -> String {
        var value = text
        while true {
            guard let open = firstToken(thinkOpenTags, in: value, from: value.startIndex) else { return value }
            let afterOpen = value.index(open.range.lowerBound, offsetBy: open.token.count)
            if let close = firstToken(thinkCloseTags, in: value, from: afterOpen) {
                value.removeSubrange(open.range.lowerBound..<close.range.upperBound)
                continue
            }
            if let protocolRange = value.range(of: protocolLine, range: afterOpen..<value.endIndex) {
                value.removeSubrange(open.range.lowerBound..<protocolRange.lowerBound)
                continue
            }
            return String(value[..<open.range.lowerBound])
        }
    }

    private static func firstToken(_ tokens: [String], in text: String, from start: String.Index) -> (token: String, range: Range<String.Index>)? {
        var best: (token: String, range: Range<String.Index>)?
        for token in tokens {
            guard let range = text.range(of: token, options: .caseInsensitive, range: start..<text.endIndex) else { continue }
            if best == nil || range.lowerBound < best!.range.lowerBound || (range.lowerBound == best!.range.lowerBound && token.count > best!.token.count) {
                best = (token, range)
            }
        }
        return best
    }

    private func acceptBody(_ body: String) -> String {
        if !body.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty { bodyHasContent = true }
        return decision?.action == .retrieve ? "" : body
    }

    private static func parse(_ json: String, allowedEntityIds: Set<String>, allowedDocumentIds: Set<String>) throws -> LiveTurnDecision {
        let data = Data(json.utf8)
        guard let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: Any] else {
            throw PhantomProtocolError(code: "control_json_invalid")
        }
        let actionRaw = stringValue(object, "action")
        guard actions.contains(actionRaw), let action = LiveCopilotAction(rawValue: actionRaw) else {
            throw PhantomProtocolError(code: "control_enum_invalid")
        }
        let questionType = coerce(stringValue(object, "questionType"), allowed: questionTypes, fallback: "unknown")
        let intent = coerce(stringValue(object, "intent"), allowed: intents, fallback: "general")
        var answerBasis = coerce(stringValue(object, "answerBasis"), allowed: bases, fallback: "universal_knowledge")
        var entityType = coerce(stringValue(object, "entityType"), allowed: entityTypes, fallback: "none")
        var entityId = String(stringValue(object, "entityId").prefix(160))
        if !entityId.isEmpty, !allowedEntityIds.contains(entityId) {
            entityId = ""
            entityType = "none"
        }
        var retrievalQuery = String(stringValue(object, "retrievalQuery").prefix(500))
        let preferredDocumentIds = stringArray(object, "preferredDocumentIds")
            .filter { !$0.isEmpty && $0.count <= 160 }
            .reduce(into: [String]()) { result, id in
                if !result.contains(id) { result.append(id) }
            }
            .filter(allowedDocumentIds.contains)
            .prefix(8)
            .map { $0 }
        if action != .retrieve { retrievalQuery = "" }
        if action == .clarify { answerBasis = "clarification" }
        var confidence = doubleValue(object, "confidence", 0.5)
        if confidence.isNaN || confidence.isInfinite { confidence = 0.5 }
        confidence = min(max(confidence, 0), 1)
        return LiveTurnDecision(
            action: action, questionType: questionType, intent: intent, answerBasis: answerBasis,
            entityType: entityType, entityId: entityId,
            retrievalQuery: retrievalQuery.trimmingCharacters(in: .whitespacesAndNewlines),
            preferredDocumentIds: preferredDocumentIds,
            targetSeconds: clampTargetSeconds(intValue(object, "targetSeconds"), questionType: questionType),
            allowCode: boolValue(object, "allowCode"), confidence: confidence
        )
    }

    private static func lookup(_ object: [String: Any], _ key: String) -> Any? {
        if let value = object[key] { return value }
        let lowered = key.lowercased()
        return object.first { $0.key.lowercased() == lowered }?.value
    }

    private static func coerce(_ value: String, allowed: Set<String>, fallback: String) -> String {
        allowed.contains(value) ? value : fallback
    }

    private static func stringValue(_ object: [String: Any], _ key: String) -> String {
        switch lookup(object, key) {
        case let value as String: return value
        case let value as NSNumber: return value.stringValue
        default: return ""
        }
    }

    private static func intValue(_ object: [String: Any], _ key: String) -> Int {
        switch lookup(object, key) {
        case let value as Int: return value
        case let value as NSNumber: return value.intValue
        case let value as String: return Int(value) ?? 0
        default: return 0
        }
    }

    private static func doubleValue(_ object: [String: Any], _ key: String, _ fallback: Double) -> Double {
        switch lookup(object, key) {
        case let value as Double: return value
        case let value as NSNumber: return value.doubleValue
        case let value as String: return Double(value) ?? fallback
        default: return fallback
        }
    }

    private static func boolValue(_ object: [String: Any], _ key: String) -> Bool {
        switch lookup(object, key) {
        case let value as Bool: return value
        case let value as NSNumber: return value.boolValue
        case let value as String: return (value as NSString).boolValue
        default: return false
        }
    }

    private static func stringArray(_ object: [String: Any], _ key: String) -> [String] {
        (lookup(object, key) as? [Any] ?? []).compactMap { item in
            item as? String
        }
    }

    private static func clampTargetSeconds(_ value: Int, questionType: String) -> Int {
        let bounds: ClosedRange<Int>
        switch questionType {
        case "behavioral": bounds = 25...45
        case "technical": bounds = 20...40
        case "coding": bounds = 30...90
        case "system_design": bounds = 40...75
        case "product_case": bounds = 30...60
        case "motivation_fit": bounds = 20...40
        case "personal_factual": bounds = 10...30
        case "situational": bounds = 20...40
        case "clarification", "unknown": bounds = 5...20
        case "factual_lookup", "status_update", "decision_support", "objection_response",
             "risk_tradeoff", "brainstorm", "action_capture": bounds = 10...40
        default: bounds = 15...45
        }
        return min(max(value, bounds.lowerBound), bounds.upperBound)
    }

}
