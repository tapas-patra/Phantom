import Foundation

/// Maps inbound companion relay frames to PhantomStore actions and provides helpers to
/// publish the outbound desktop frames defined in the companion WebSocket contract.
/// Runs on the main actor alongside PhantomStore.
@MainActor
final class CompanionCommandHost {
    private weak var store: PhantomStore?
    private let relay: CompanionRelayClient
    private var currentRequestId: String?

    var visionSupported: () -> Bool = { true }

    init(store: PhantomStore, relay: CompanionRelayClient) {
        self.store = store
        self.relay = relay
        relay.frameHandler = { [weak self] frame in
            Task { await self?.handle(frame: frame) }
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
        case "relay.ping", "relay.peer_joined", "relay.peer_left":
            return
        case "relay.error":
            if let code = frame.string("code") {
                print("[companion] relay error: \(code)")
            }
            return
        case "capture.full":
            let displayId = frame.string("displayId")
            let attachOnly = frame.bool("attachOnly") ?? false
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await captureFull(requestId: requestId, displayId: displayId, attachOnly: attachOnly)
        case "capture.ask":
            let displayId = frame.string("displayId")
            let prompt = frame.string("prompt")
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await captureAsk(requestId: requestId, displayId: displayId, prompt: prompt)
        case "chat.send":
            let text = frame.string("text") ?? ""
            let requestId = frame.string("requestId") ?? frame.id ?? UUID().uuidString
            await chatSend(requestId: requestId, text: text)
        case "chat.cancel":
            store?.cancelCurrentRequest()
            if let requestId = frame.string("requestId") {
                sendChatCancelled(requestId: requestId)
            }
        case "chat.new_topic":
            store?.startNewTopic()
            await publishSnapshot()
        case "display.select":
            if let displayId = frame.string("displayId") {
                store?.companionSelectedDisplayId = displayId
            }
        default:
            break
        }
    }

    // MARK: - Inbound command handlers

    private func captureAsk(requestId: String, displayId: String?, prompt: String?) async {
        guard let store else { return }
        currentRequestId = requestId
        if await !store.companionHasActiveLock() {
            sendCaptureFailed(requestId: requestId, code: "lock_missing", message: "Desktop does not hold an active interview lock.")
            return
        }
        guard visionSupported() else {
            sendCaptureFailed(requestId: requestId, code: "vision_unsupported", message: "Current model does not support vision.")
            return
        }
        do {
            let data = try ScreenshotCapture.captureDisplay(id: displayId)
            sendCaptureStarted(requestId: requestId, displayId: displayId ?? "")
            while store.attachedScreenshots.count >= PhantomStore.maxAttachedScreenshots {
                store.attachedScreenshots.removeFirst()
            }
            store.attachedScreenshots.append(data)
            if let p = prompt, !p.isEmpty {
                store.prompt = p
            } else {
                store.prompt = "Please analyze this screenshot."
            }
            store.companionRequestId = requestId
            store.companionDeltaHandler = { [weak self] delta in
                self?.sendChatDelta(requestId: requestId, text: delta)
            }
            store.companionTurnFinishedHandler = { [weak self] succeeded in
                if succeeded {
                    self?.sendChatCompleted(requestId: requestId)
                } else {
                    self?.sendChatFailed(requestId: requestId, code: "chat_failed", message: "Chat request failed.")
                }
                self?.publishSnapshotSync()
            }
            store.send()
        } catch ScreenshotError.permissionDenied {
            sendCaptureFailed(requestId: requestId, code: "capture_permission_missing", message: "Screen Recording permission missing.")
        } catch {
            sendCaptureFailed(requestId: requestId, code: "capture_failed", message: "Headless capture failed.")
        }
    }

    private func captureFull(requestId: String, displayId: String?, attachOnly: Bool) async {
        guard let store else { return }
        currentRequestId = requestId
        if await !store.companionHasActiveLock() {
            sendCaptureFailed(requestId: requestId, code: "lock_missing", message: "Desktop does not hold an active interview lock.")
            return
        }
        guard visionSupported() else {
            sendCaptureFailed(requestId: requestId, code: "vision_unsupported", message: "Current model does not support vision.")
            return
        }
        do {
            let data = try ScreenshotCapture.captureDisplay(id: displayId)
            sendCaptureStarted(requestId: requestId, displayId: displayId ?? "")
            while store.attachedScreenshots.count >= PhantomStore.maxAttachedScreenshots {
                store.attachedScreenshots.removeFirst()
            }
            store.attachedScreenshots.append(data)
            if !attachOnly {
                store.prompt = "Please analyze this screenshot."
                store.companionRequestId = requestId
                store.companionDeltaHandler = { [weak self] delta in
                    self?.sendChatDelta(requestId: requestId, text: delta)
                }
                store.companionTurnFinishedHandler = { [weak self] succeeded in
                    if succeeded {
                        self?.sendChatCompleted(requestId: requestId)
                    } else {
                        self?.sendChatFailed(requestId: requestId, code: "chat_failed", message: "Chat request failed.")
                    }
                    self?.publishSnapshotSync()
                }
                store.send()
            }
        } catch ScreenshotError.permissionDenied {
            sendCaptureFailed(requestId: requestId, code: "capture_permission_missing", message: "Screen Recording permission missing.")
        } catch {
            sendCaptureFailed(requestId: requestId, code: "capture_failed", message: "Headless capture failed.")
        }
    }

    private func chatSend(requestId: String, text: String) async {
        guard let store else { return }
        currentRequestId = requestId
        if await !store.companionHasActiveLock() {
            sendChatFailed(requestId: requestId, code: "lock_missing", message: "Desktop does not hold an active interview lock.")
            return
        }
        store.prompt = text
        store.companionRequestId = requestId
        store.companionDeltaHandler = { [weak self] delta in
            self?.sendChatDelta(requestId: requestId, text: delta)
        }
        store.companionTurnFinishedHandler = { [weak self] succeeded in
            if succeeded {
                self?.sendChatCompleted(requestId: requestId)
            } else {
                self?.sendChatFailed(requestId: requestId, code: "chat_failed", message: "Chat request failed.")
            }
            self?.publishSnapshotSync()
        }
        store.send()
    }

    // MARK: - Outbound frames

    func sendDesktopHello() async {
        guard let store else { return }
        let displays = ScreenshotCapture.listDisplays().map { CompanionDisplay(id: $0.id, name: $0.name, isDefault: $0.isDefault) }
        let lockExpiry = await store.companionLockExpiresAt()
        let body: [String: Any] = [
            "status": store.isSending ? "thinking" : "ready",
            "model": store.selectedModelId,
            "provider": store.selectedProviderId,
            "vision": visionSupported(),
            "displays": displays.map { ["id": $0.id, "name": $0.name, "isDefault": $0.isDefault] },
            "lockExpiresAtUtc": lockExpiry?.ISO8601Format() ?? ""
        ]
        sendEnvelope(type: "desktop.hello", body: body)
    }

    func sendCaptureStarted(requestId: String, displayId: String) {
        sendEnvelope(type: "capture.started", body: ["requestId": requestId, "displayId": displayId])
    }

    func sendCaptureFailed(requestId: String, code: String, message: String) {
        sendEnvelope(type: "capture.failed", body: ["requestId": requestId, "code": code, "message": message])
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

    func publishSnapshot() async {
        publishSnapshotSync()
    }

    private func publishSnapshotSync() {
        guard let store else { return }
        let turns = store.messages.suffix(20).map { CompanionTurn(role: $0.role, text: $0.content, atUtc: $0.createdAtUtc ?? Date()) }
        let displays = ScreenshotCapture.listDisplays().map { CompanionDisplay(id: $0.id, name: $0.name, isDefault: $0.isDefault) }
        let body: [String: Any] = [
            "pairingId": relay.pairingId,
            "desktopStatus": store.isSending ? "thinking" : "ready",
            "provider": store.selectedProviderId,
            "model": store.selectedModelId,
            "vision": visionSupported(),
            "displays": displays.map { ["id": $0.id, "name": $0.name, "isDefault": $0.isDefault] },
            "selectedDisplayId": store.companionSelectedDisplayId,
            "turns": turns.map { ["role": $0.role, "text": $0.text, "atUtc": $0.atUtc.ISO8601Format()] }
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
