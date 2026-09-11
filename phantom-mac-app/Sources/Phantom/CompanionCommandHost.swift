import Foundation

/// Maps inbound companion relay frames to PhantomStore actions and provides helpers to
/// publish the outbound desktop frames defined in the companion WebSocket contract.
/// Runs on the main actor alongside PhantomStore.
@MainActor
final class CompanionCommandHost {
    private weak var store: PhantomStore?
    private let relay: CompanionRelayClient
    private var currentRequestId: String?
    // True when a chat.cancel was received for the in-flight turn. The turn-finished
    // handler emits chat.cancelled (not chat.failed) when this is set (C4).
    private var pendingCancel = false

    var visionSupported: () -> Bool = { true }
    /// Called with the relay.error code when a terminal error (pairing_revoked /
    /// replaced / server_shutdown) arrives, so the orchestrator can stop reconnecting (M18).
    var onTerminalRelayError: ((String) -> Void)?

    init(store: PhantomStore, relay: CompanionRelayClient) {
        self.store = store
        self.relay = relay
        relay.frameHandler = { [weak self] frame in
            Task { await self?.handle(frame: frame) }
        }
        store.companionSessionStartedHandler = { [weak self] in
            Task { await self?.sendDesktopHello(); await self?.publishSnapshot() }
        }
    }

    func detach() {
        relay.frameHandler = nil
    }

    private func handle(frame: CompanionRelayFrame) async {
        guard let type = frame.type else { return }
        switch type {
        case "session.hello":
            await sendDesktopHello()
            await publishSnapshot()
        case "relay.ping":
            sendEnvelope(type: "relay.pong", body: [:])
            return
        case "relay.peer_joined", "relay.peer_left":
            return
        case "relay.error":
            // relay.error sends code/message at the envelope top level (M18).
            let code = frame.code ?? frame.string("code") ?? ""
            if let message = frame.message, !message.isEmpty {
                print("[companion] relay error: \(code) \(message)")
            } else {
                print("[companion] relay error: \(code)")
            }
            if code == "pairing_revoked" || code == "replaced" || code == "server_shutdown" {
                onTerminalRelayError?(code)
            }
            return
        case "capture.full":
            let displayId = resolveDisplayId(frame.string("displayId"))
            let attachOnly = frame.bool("attachOnly") ?? false
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await captureFull(requestId: requestId, displayId: displayId, attachOnly: attachOnly)
        case "capture.ask":
            let displayId = resolveDisplayId(frame.string("displayId"))
            let prompt = frame.string("prompt")
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await captureAsk(requestId: requestId, displayId: displayId, prompt: prompt)
        case "chat.send":
            let text = frame.string("text") ?? ""
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await chatSend(requestId: requestId, text: text)
        case "chat.cancel":
            // Only cancel when the cancel targets the in-flight request. A stale cancel
            // arriving after a new request started must not kill the newer turn (H2).
            let cancelRequestId = frame.string("requestId")
            if let cancelRequestId, let currentRequestId, cancelRequestId != currentRequestId {
                // Stale cancel for a different request — ignore.
                return
            }
            pendingCancel = true
            store?.cancelCurrentRequest()
        case "chat.new_topic":
            // Lightweight topic reset that does NOT release the interview lock (C10).
            store?.startNewTopicCompanion()
            await publishSnapshot()
        case "display.select":
            if let displayId = frame.string("displayId") {
                store?.companionSelectedDisplayId = displayId
            }
        case "voice.start":
            if store?.isListening != true {
                store?.toggleVoiceInput()
            }
        case "voice.stop":
            if store?.isListening == true {
                store?.toggleVoiceInput()
            }
        case "runtime.select":
            applyRuntimeSelect(provider: frame.string("provider"), model: frame.string("model"))
        case "capture.remove":
            if let index = frame.int("index") {
                store?.removeScreenshot(at: index)
                publishSnapshotSync()
            }
        case "capture.clear":
            store?.removeScreenshot(at: nil)
            publishSnapshotSync()
        default:
            break
        }
    }

    /// Resolves a phone-supplied displayId, falling back to the last display.select
    /// choice (spec §5.1, M12).
    private func resolveDisplayId(_ displayId: String?) -> String? {
        if let displayId, !displayId.isEmpty { return displayId }
        let selected = store?.companionSelectedDisplayId ?? ""
        return selected.isEmpty ? nil : selected
    }

    private func applyRuntimeSelect(provider: String?, model: String?) {
        guard let store else { return }
        if let provider, !provider.isEmpty {
            store.selectedProviderId = provider
        }
        if let model, !model.isEmpty {
            store.selectedModelId = model
        }
        Task { await sendDesktopHello(); await publishSnapshot() }
    }

    // MARK: - Inbound command handlers

    private func captureAsk(requestId: String, displayId: String?, prompt: String?) async {
        guard let store else { return }
        currentRequestId = requestId
        pendingCancel = false
        guard visionSupported() else {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "vision_unsupported", message: "Current model does not support vision.")
            return
        }
        guard store.attachedScreenshots.count < PhantomStore.maxAttachedScreenshots else {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "attachment_limit", message: "You can attach up to 3 screenshots.")
            return
        }
        do {
            let data = try ScreenshotCapture.captureDisplay(id: displayId)
            sendCaptureStarted(requestId: requestId, displayId: displayId ?? "")
            store.companionIsCapturing = true
            defer { store.companionIsCapturing = false }
            sendCaptureCompleted(requestId: requestId, from: data)
            store.attachedScreenshots.append(data)
            if let p = prompt, !p.isEmpty {
                store.prompt = p
            } else {
                store.prompt = "Please analyze this screenshot."
            }
            store.companionRequestId = requestId
            sendChatStarted(requestId: requestId, turnId: requestId)
            store.companionDeltaHandler = { [weak self] delta in
                self?.sendChatDelta(requestId: requestId, text: delta)
            }
            store.companionTurnFinishedHandler = { [weak self] succeeded in
                self?.finishTurn(requestId: requestId, succeeded: succeeded)
            }
            store.send()
        } catch ScreenshotError.permissionDenied {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "capture_permission_missing", message: "Screen Recording permission missing.")
        } catch {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "capture_failed", message: "Headless capture failed.")
        }
    }

    private func captureFull(requestId: String, displayId: String?, attachOnly: Bool) async {
        guard let store else { return }
        currentRequestId = requestId
        pendingCancel = false
        guard visionSupported() else {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "vision_unsupported", message: "Current model does not support vision.")
            return
        }
        guard store.attachedScreenshots.count < PhantomStore.maxAttachedScreenshots else {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "attachment_limit", message: "You can attach up to 3 screenshots.")
            return
        }
        do {
            let data = try ScreenshotCapture.captureDisplay(id: displayId)
            sendCaptureStarted(requestId: requestId, displayId: displayId ?? "")
            store.companionIsCapturing = true
            defer { store.companionIsCapturing = false }
            sendCaptureCompleted(requestId: requestId, from: data)
            store.attachedScreenshots.append(data)
            if !attachOnly {
                store.prompt = "Please analyze this screenshot."
                store.companionRequestId = requestId
                sendChatStarted(requestId: requestId, turnId: requestId)
                store.companionDeltaHandler = { [weak self] delta in
                    self?.sendChatDelta(requestId: requestId, text: delta)
                }
                store.companionTurnFinishedHandler = { [weak self] succeeded in
                    self?.finishTurn(requestId: requestId, succeeded: succeeded)
                }
                store.send()
            } else {
                // Attach-only: no chat turn. Publish a snapshot so the phone sees state.
                publishSnapshotSync()
            }
        } catch ScreenshotError.permissionDenied {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "capture_permission_missing", message: "Screen Recording permission missing.")
        } catch {
            store.companionHasError = true
            sendCaptureFailed(requestId: requestId, code: "capture_failed", message: "Headless capture failed.")
        }
    }

    private func chatSend(requestId: String, text: String) async {
        guard let store else { return }
        currentRequestId = requestId
        pendingCancel = false
        if text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            store.companionHasError = true
            sendChatFailed(requestId: requestId, code: "chat_failed", message: "Empty message.")
            return
        }
        store.prompt = text
        store.companionRequestId = requestId
        sendChatStarted(requestId: requestId, turnId: requestId)
        store.companionDeltaHandler = { [weak self] delta in
            self?.sendChatDelta(requestId: requestId, text: delta)
        }
        store.companionTurnFinishedHandler = { [weak self] succeeded in
            self?.finishTurn(requestId: requestId, succeeded: succeeded)
        }
        store.send()
    }

    /// Turn-finished hook shared by all chat-bearing commands. Emits the correct
    /// chat.* frame based on outcome and the pendingCancel flag (C4), then publishes a snapshot.
    func completeDesktopOriginatedTurn(requestId: String, succeeded: Bool) {
        finishTurn(requestId: requestId, succeeded: succeeded)
    }

    private func finishTurn(requestId: String, succeeded: Bool) {
        if pendingCancel {
            sendChatCancelled(requestId: requestId)
        } else if succeeded {
            store?.companionHasError = false // clear transient error on success (H1)
            sendChatCompleted(requestId: requestId)
        } else {
            store?.companionHasError = true // surface error status until next success (H1)
            sendChatFailed(requestId: requestId, code: "chat_failed", message: "Chat request failed.")
        }
        pendingCancel = false
        publishSnapshotSync()
    }

    // MARK: - Outbound frames

    func sendDesktopHello() async {
        guard let store else { return }
        let displays = ScreenshotCapture.listDisplays().map { CompanionDisplay(id: $0.id, name: $0.name, isDefault: $0.isDefault) }
        let lockExpiry = await store.companionLockExpiresAt()
        let body: [String: Any] = [
            "status": desktopStatus(),
            "model": store.selectedModelId,
            "provider": store.selectedProviderId,
            "vision": visionSupported(),
            "displays": displays.map { ["id": $0.id, "name": $0.name, "isDefault": $0.isDefault] },
            "lockExpiresAtUtc": lockExpiry?.ISO8601Format() ?? "",
            "attachmentCount": store.attachedScreenshotCount,
            "attachments": store.companionAttachmentPayloads(),
            "providers": store.companionProviderCatalog()
        ]
        sendEnvelope(type: "desktop.hello", body: body)
    }

    /// Desktop status mapping (spec §5.2): offline | connecting | idle | ready | capturing | thinking | error (M17).
    private func desktopStatus() -> String {
        guard let store else { return "offline" }
        if !store.companionEnabled { return "offline" }
        if store.companionRelayState != .connected { return "connecting" }
        if !store.companionHasActiveLockSync && !store.companionInterviewActive { return "idle" }
        if store.companionHasError { return "error" } // surface last capture/chat failure until next success (H1)
        if store.companionIsCapturing { return "capturing" }
        if store.isSending { return "thinking" }
        return "ready"
    }

    func sendCaptureStarted(requestId: String, displayId: String) {
        sendEnvelope(type: "capture.started", body: ["requestId": requestId, "displayId": displayId])
    }

    /// Sends the capture.completed frame with the JPEG thumbnail so the phone can render
    /// the screenshot preview in the chat turn (spec §5.2). Previously macOS skipped this
    /// frame entirely, so the phone never received the captured image (C2).
    func sendCaptureCompleted(requestId: String, from data: Data) {
        let payload = ScreenshotCapture.captureCompletedPayload(from: data)
        let width = payload?.width ?? 0
        let height = payload?.height ?? 0
        let thumb = payload?.thumbnailJpegBase64
        var body: [String: Any] = [
            "requestId": requestId,
            "width": width,
            "height": height
        ]
        if let thumb { body["thumbnailJpegBase64"] = thumb }
        sendEnvelope(type: "capture.completed", body: body)
    }

    func sendCaptureFailed(requestId: String, code: String, message: String) {
        sendEnvelope(type: "capture.failed", body: ["requestId": requestId, "code": code, "message": message])
    }

    func sendChatStarted(requestId: String, turnId: String) {
        sendEnvelope(type: "chat.started", body: ["requestId": requestId, "turnId": turnId])
    }

    func sendChatDelta(requestId: String, text: String) {
        sendEnvelope(type: "chat.delta", body: ["requestId": requestId, "text": text])
    }

    func sendChatCompleted(requestId: String) {
        sendEnvelope(type: "chat.completed", body: ["requestId": requestId])
    }

    func sendChatFailed(requestId: String, code: String, message: String) {
        sendEnvelope(type: "chat.failed", body: ["requestId": requestId, "code": code, "message": message])
    }

    func sendChatCancelled(requestId: String) {
        sendEnvelope(type: "chat.cancelled", body: ["requestId": requestId])
    }

    func sendVoiceTranscript(text: String) {
        sendEnvelope(type: "voice.transcript", body: ["text": text])
    }

    func publishSnapshot() async {
        publishSnapshotSync()
    }

    private func publishSnapshotSync() {
        guard let store else { return }
        let turns = store.messages.suffix(20)
            .filter { $0.role == "user" || $0.role == "assistant" }
            .map { CompanionTurn(role: $0.role, text: $0.content, atUtc: $0.createdAtUtc ?? Date()) }
        let displays = ScreenshotCapture.listDisplays().map { CompanionDisplay(id: $0.id, name: $0.name, isDefault: $0.isDefault) }
        // session.snapshot body omits pairingId (spec §5.2: same shape as /sessions/current minus pairingId) (M11).
        let body: [String: Any] = [
            "desktopStatus": desktopStatus(),
            "provider": store.selectedProviderId,
            "model": store.selectedModelId,
            "vision": visionSupported(),
            "displays": displays.map { ["id": $0.id, "name": $0.name, "isDefault": $0.isDefault] },
            "selectedDisplayId": store.companionSelectedDisplayId,
            "turns": turns.map { ["role": $0.role, "text": $0.text, "atUtc": $0.atUtc.ISO8601Format()] },
            "attachmentCount": store.attachedScreenshotCount,
            "attachments": store.companionAttachmentPayloads(),
            "providers": store.companionProviderCatalog()
        ]
        sendEnvelope(type: "session.snapshot", body: body)
    }

    private func sendEnvelope(type: String, body: [String: Any]) {
        var envelope: [String: Any] = [
            "v": 1,
            "id": UUID().uuidString,
            "type": type,
            "ts": ISO8601DateFormatter().string(from: Date()),
            "pairingId": relay.pairingId,
            "role": "desktop"
        ]
        envelope["body"] = body
        guard let data = try? JSONSerialization.data(withJSONObject: envelope),
              let json = String(data: data, encoding: .utf8) else { return }
        relay.sendRaw(json)
    }
}
