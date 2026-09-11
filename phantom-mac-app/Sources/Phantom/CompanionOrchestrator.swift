import Foundation

/// Owns the Companion Mode lifecycle on macOS: fetches a relay ticket, opens the relay
/// socket, wires inbound frames to PhantomStore via CompanionCommandHost, and exposes
/// the current relay state for the settings UI. Hides the overlay while companion mode
/// is active (spec §8.2) via the store's overlay-hide hook.
@MainActor
final class CompanionOrchestrator {
    private let backend: BackendClient
    private weak var store: PhantomStore?
    private var relay: CompanionRelayClient?
    private var commandHost: CompanionCommandHost?
    private var activePairingId: String?
    private var accessToken: String?
    private(set) var enabled = false
    private(set) var state: CompanionRelayState = .disconnected

    init(backend: BackendClient, store: PhantomStore) {
        self.backend = backend
        self.store = store
    }

    /// Reconciles the orchestrator with the current settings. Starts the relay when enabled
    /// and a pairing id is present; stops it otherwise.
    func reconcile(enabled: Bool, pairingId: String?, accessToken: String?) async {
        if !enabled || pairingId == nil || pairingId?.isEmpty == true {
            await stop()
            return
        }
        guard let pairingId, let accessToken else { return }
        if enabled, let activePairingId, activePairingId == pairingId, relay != nil {
            return
        }
        await stop()
        activePairingId = pairingId
        self.accessToken = accessToken

        // Ticket provider fetches a fresh ticket on every connect (spec §9).
        let backend = self.backend
        let token = accessToken
        let ticketProvider: () async throws -> CompanionRelayTicket = {
            try await backend.createRelayTicket(accessToken: token, pairingId: pairingId, role: "desktop")
        }

        // Validate the first ticket fetch before flipping enabled / hiding overlay (M19).
        do {
            _ = try await ticketProvider()
        } catch {
            print("[companion] relay ticket request failed: \(error.localizedDescription)")
            self.accessToken = nil
            activePairingId = nil
            return
        }

        let relay = CompanionRelayClient(ticketProvider: ticketProvider, pairingId: pairingId, role: "desktop")
        guard let store else { return }
        let host = CompanionCommandHost(store: store, relay: relay)
        host.visionSupported = { [weak store] in
            store?.selectedModelSupportsVision ?? false
        }
        host.onTerminalRelayError = { [weak self] code in
            self?.handleTerminalRelayError(code: code)
        }
        relay.stateHandler = { [weak self] state in
            guard let self else { return }
            self.state = state
            self.store?.companionRelayState = state
            if state == .connected {
                // Proactively announce the desktop so the phone sees presence + snapshot
                // immediately, without waiting for a session.hello from the phone.
                Task { [weak host] in
                    await host?.sendDesktopHello()
                    await host?.publishSnapshot()
                }
            }
        }
        relay.start()
        self.relay = relay
        self.commandHost = host
        self.enabled = true
        // Hide the overlay via the existing hide path (spec §8.2). Must not activate Phantom.
        store.setCompanionOverlayHidden(true)
        print("[companion] started for pairing \(pairingId)")
    }

    private func handleTerminalRelayError(code: String) {
        // pairing_revoked / replaced / server_shutdown are terminal — stop reconnecting
        // so we don't hammer a dead/invalid ticket (M18).
        if code == "pairing_revoked" || code == "replaced" || code == "server_shutdown" {
            print("[companion] terminal relay error '\(code)' — stopping companion mode.")
            Task { await stop() }
        }
    }

    func stop() async {
        enabled = false
        commandHost?.detach()
        relay?.stop()
        relay = nil
        commandHost = nil
        activePairingId = nil
        accessToken = nil
        state = .disconnected
        // Restore the overlay only if companion hid it.
        store?.setCompanionOverlayHidden(false)
    }
}
