import Foundation

@MainActor
final class LiveCopilotOrchestrator {
    typealias ModelStream = (@escaping (String) -> Void, @escaping () -> Void) async throws -> String

    func execute(
        allowedEntityIds: [String],
        allowedDocumentIds: [String],
        firstModel: @escaping ModelStream,
        retrieve: @escaping (LiveTurnDecision) async throws -> LiveCopilotRetrieval,
        secondModel: @escaping (LiveTurnDecision, LiveCopilotRetrieval) -> ModelStream,
        publish: @escaping (String) -> Void,
        resetPublishedAttempt: @escaping () -> Void,
        protocolRejected: ((String) -> Void)? = nil
    ) async throws -> LiveCopilotResult {
        var modelCalls = 0
        var protocolRetries = 0
        var parser = PhantomControlFrameParser(allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
        var firstResponse = ""

        while true {
            modelCalls += 1
            do {
                var streamProtocolError: PhantomProtocolError?
                firstResponse = try await firstModel({ chunk in
                    guard streamProtocolError == nil else { return }
                    do {
                        let visible = try parser.feed(chunk)
                        if !visible.isEmpty { publish(visible) }
                    } catch let error as PhantomProtocolError {
                        streamProtocolError = error
                    } catch {
                        streamProtocolError = PhantomProtocolError(code: "control_json_invalid")
                    }
                }, {
                    parser = PhantomControlFrameParser(allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
                    resetPublishedAttempt()
                })
                if let streamProtocolError { throw streamProtocolError }
                if !parser.hasReceivedChunks, !firstResponse.isEmpty {
                    let visible = try parser.feed(firstResponse)
                    if !visible.isEmpty { publish(visible) }
                }
                _ = try parser.complete()
                break
            } catch let error as PhantomProtocolError where protocolRetries == 0 {
                protocolRejected?(error.code)
                protocolRetries += 1
                parser = PhantomControlFrameParser(allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
                resetPublishedAttempt()
            }
        }

        let decision = try parser.complete()
        if protocolRetries > 0, decision.action == .retrieve {
            throw PhantomProtocolError(code: "control_repair_retrieve_invalid")
        }
        if decision.action != .retrieve {
            return LiveCopilotResult(
                answer: Self.extractBody(firstResponse), decision: decision, modelCallCount: modelCalls,
                protocolRetryCount: protocolRetries, retrievalStatus: "not_requested", activeEvidence: []
            )
        }

        let retrieval = try await retrieve(decision)
        var finalResponse = ""
        modelCalls += 1
        let completed = try await secondModel(decision, retrieval)({ chunk in
            finalResponse += chunk
            publish(chunk)
        }, {
            finalResponse = ""
            resetPublishedAttempt()
        })
        if finalResponse.isEmpty { finalResponse = completed }
        guard !finalResponse.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw BackendError.server("The AI provider returned an empty response.")
        }
        return LiveCopilotResult(
            answer: finalResponse, decision: decision, modelCallCount: modelCalls,
            protocolRetryCount: protocolRetries, retrievalStatus: retrieval.status, activeEvidence: retrieval.snippets
        )
    }

    static func extractBody(_ response: String) -> String {
        guard let range = response.range(of: PhantomControlFrameParser.bodyDelimiter) else { return "" }
        return String(response[range.upperBound...])
    }
}
