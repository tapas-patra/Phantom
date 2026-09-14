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
        protocolRejected: ((String) -> Void)? = nil,
        decisionParsed: ((LiveTurnDecision, Int) -> Void)? = nil
    ) async throws -> LiveCopilotResult {
        var modelCalls = 0
        var protocolRetries = 0
        var parser = PhantomControlFrameParser(allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
        var firstResponse = ""
        var fallbackBuffer = ""

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
            } catch let error as PhantomProtocolError {
                var candidate = parser.fallbackAnswerText()
                if candidate.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    candidate = PhantomControlFrameParser.extractBareAnswer(firstResponse)
                }
                if !candidate.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    fallbackBuffer = candidate
                }
                if protocolRetries == 0 {
                    protocolRejected?(error.code)
                    protocolRetries += 1
                    if PhantomControlFrameParser.canFallback(error.code), Self.isCompleteAnswer(fallbackBuffer) {
                        protocolRejected?("control_frame_fallback")
                        let fallbackDecision = PhantomControlFrameParser.fallbackAnswerDecision()
                        decisionParsed?(fallbackDecision, modelCalls)
                        publish(fallbackBuffer)
                        return LiveCopilotResult(
                            answer: fallbackBuffer, decision: fallbackDecision, modelCallCount: modelCalls,
                            protocolRetryCount: protocolRetries, retrievalStatus: "not_requested", activeEvidence: []
                        )
                    }
                    parser = PhantomControlFrameParser(allowedEntityIds: allowedEntityIds, allowedDocumentIds: allowedDocumentIds)
                    resetPublishedAttempt()
                    continue
                }
                if PhantomControlFrameParser.canFallback(error.code), Self.isCompleteAnswer(fallbackBuffer) {
                    protocolRejected?("control_frame_fallback")
                    let fallbackDecision = PhantomControlFrameParser.fallbackAnswerDecision()
                    decisionParsed?(fallbackDecision, modelCalls)
                    publish(fallbackBuffer)
                    return LiveCopilotResult(
                        answer: fallbackBuffer, decision: fallbackDecision, modelCallCount: modelCalls,
                        protocolRetryCount: protocolRetries, retrievalStatus: "not_requested", activeEvidence: []
                    )
                }
                throw error
            }
        }

        let decision = try parser.complete()
        decisionParsed?(decision, modelCalls)
        if protocolRetries > 0, decision.action == .retrieve {
            throw PhantomProtocolError(code: "control_repair_retrieve_invalid")
        }
        if decision.action != .retrieve {
            let answer = PhantomControlFrameParser.resolveAnswer(firstResponse)
            guard Self.isCompleteAnswer(answer) else {
                throw BackendError.server("The AI provider returned an incomplete response.")
            }
            return LiveCopilotResult(
                answer: answer, decision: decision, modelCallCount: modelCalls,
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
        guard Self.isCompleteAnswer(finalResponse) else {
            throw BackendError.server("The AI provider returned an incomplete response.")
        }
        return LiveCopilotResult(
            answer: finalResponse, decision: decision, modelCallCount: modelCalls,
            protocolRetryCount: protocolRetries, retrievalStatus: retrieval.status, activeEvidence: retrieval.snippets
        )
    }

    static func extractBody(_ response: String) -> String {
        PhantomControlFrameParser.extractAnswerBody(response)
    }

    nonisolated static func isCompleteAnswer(_ response: String) -> Bool {
        guard !response.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return false }
        return response.components(separatedBy: "```").count % 2 == 1
    }
}
