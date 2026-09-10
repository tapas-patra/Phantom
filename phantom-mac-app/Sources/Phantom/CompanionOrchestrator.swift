import Foundation

/// Owns the Companion Mode lifecycle on macOS: fetches a relay ticket, opens the relay
/// socket, wires inbound frames to PhantomStore via CompanionCommandHost, and exposes
/// the current relay state for the settings UI.
@MainActor
final class CompanionOrchestrator {
    private let backend: BackendClient
    private weak var store: PhantomStore?
    private var relay: CompanionRelayClient?
    private var commandHost: CompanionCommandHost?
    private var activePairingId: String?
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
            self.enabled = true
            return
        }
        await stop()
        self.enabled = true
        activePairingId = pairingId

        let ticket: CompanionRelayTicket
        do {
            ticket = try await backend.createRelayTicket(accessToken: accessToken, pairingId: pairingId, role: "desktop")
        } catch {
            print("[companion] relay ticket request failed: \(error.localizedDescription)")
            return
        }

        guard let url = URL(string: ticket.relayUrl) else {
            print("[companion] bad relay url: \(ticket.relayUrl)")
            return
        }
        let relay = CompanionRelayClient(relayUrl: url, ticket: ticket.ticket, pairingId: pairingId, role: "desktop")
        guard let store else { return }
        let host = CompanionCommandHost(store: store, relay: relay)
        host.visionSupported = { [weak store] in
            store?.selectedModelSupportsVision ?? false
        }
        relay.stateHandler = { [weak self] state in
            self?.state = state
        }
        relay.start()
        self.relay = relay
        self.commandHost = host
        print("[companion] started for pairing \(pairingId)")
    }

    func stop() async {
        enabled = false
        commandHost?.detach()
        relay?.stop()
        relay = nil
        commandHost = nil
        activePairingId = nil
        state = .disconnected
    }
}
