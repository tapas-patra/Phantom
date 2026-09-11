import Foundation

/// WebSocket relay client for Companion Mode on macOS. Connects to the hosted relay with a
/// short-lived ticket using URLSessionWebSocketTask, sends/receives JSON envelopes, and
/// reconnects with a capped backoff. A fresh ticket is fetched from `ticketProvider` on
/// every connect attempt (spec §9) so reconnects after a drop do not fail on a consumed
/// ticket. Inbound frames are surfaced via `frameHandler`.
@MainActor
final class CompanionRelayClient {
    private static let subprotocol = "phantom.companion.v1"
    private static let receiveTimeout: TimeInterval = 90
    private static let pingInterval: TimeInterval = 15
    private static let backoff: [TimeInterval] = [1, 2, 4, 8, 15]

    private let ticketProvider: () async throws -> CompanionRelayTicket
    let pairingId: String
    private let role: String
    // Reuse a single URLSession across reconnects to avoid leaking sessions (M15).
    private let session = URLSession(configuration: .default)
    private var task: URLSessionWebSocketTask?
    private var pingTask: Task<Void, Never>?
    private var receiveTask: Task<Void, Never>?
    private var watchdogTask: Task<Void, Never>?
    private var backoffIndex = 0
    private var stopped = false
    private var lastReceivedAt = Date()

    private(set) var state: CompanionRelayState = .disconnected

    var frameHandler: ((CompanionRelayFrame) -> Void)?
    var stateHandler: ((CompanionRelayState) -> Void)?

    init(ticketProvider: @escaping () async throws -> CompanionRelayTicket, pairingId: String, role: String) {
        self.ticketProvider = ticketProvider
        self.pairingId = pairingId
        self.role = role
    }

    func start() {
        stopped = false
        connect()
    }

    func stop() {
        stopped = true
        pingTask?.cancel()
        receiveTask?.cancel()
        watchdogTask?.cancel()
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        session.invalidateAndCancel()
        updateState(.disconnected)
    }

    func send(frame: CompanionRelayFrame) {
        guard let task, task.state == .running else { return }
        do {
            let data = try JSONEncoder().encode(frame)
            task.send(.data(data)) { error in
                if let error {
                    print("[companion] relay send error: \(error.localizedDescription)")
                }
            }
        } catch {
            print("[companion] relay encode error: \(error.localizedDescription)")
        }
    }

    func sendRaw(_ json: String) {
        guard let task, task.state == .running, let data = json.data(using: .utf8) else { return }
        task.send(.data(data)) { _ in }
    }

    private func connect() {
        guard !stopped else { return }
        updateState(.connecting)
        Task { [weak self] in
            guard let self else { return }
            let ticket: CompanionRelayTicket
            do {
                ticket = try await self.ticketProvider()
            } catch {
                print("[companion] relay ticket fetch failed: \(error.localizedDescription)")
                if Self.isTerminalPairingFailure(error.localizedDescription) {
                    self.stopped = true
                    var errorFrame = CompanionRelayFrame()
                    errorFrame.type = "relay.error"
                    errorFrame.code = "pairing_revoked"
                    errorFrame.message = error.localizedDescription
                    self.frameHandler?(errorFrame)
                    self.updateState(.disconnected)
                    return
                }
                if !self.stopped { self.scheduleReconnect() }
                return
            }
            guard !self.stopped else { return }
            self.openSocket(with: ticket)
        }
    }

    private func openSocket(with ticket: CompanionRelayTicket) {
        guard let url = URL(string: ticket.relayUrl) else {
            print("[companion] bad relay url: \(ticket.relayUrl)")
            updateState(.disconnected)
            scheduleReconnect()
            return
        }
        var components = URLComponents(url: url, resolvingAgainstBaseURL: false)
        var queryItems = components?.queryItems ?? []
        queryItems.append(URLQueryItem(name: "ticket", value: ticket.ticket))
        components?.queryItems = queryItems
        guard let ticketedUrl = components?.url else {
            print("[companion] bad relay url")
            updateState(.disconnected)
            scheduleReconnect()
            return
        }
        let wsTask = session.webSocketTask(with: ticketedUrl, protocols: [Self.subprotocol])
        task = wsTask
        wsTask.resume()
        backoffIndex = 0
        lastReceivedAt = Date()
        // Stay `.connecting` until the first frame confirms the handshake (M14).
        startPing()
        startReceive()
        startWatchdog()
    }

    private func startPing() {
        pingTask?.cancel()
        pingTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled, !self.stopped {
                try? await Task.sleep(nanoseconds: UInt64(Self.pingInterval * 1_000_000_000))
                guard !self.stopped, self.task != nil else { break }
                let ts = ISO8601DateFormatter().string(from: Date())
                self.sendRaw("{\"v\":1,\"type\":\"relay.ping\",\"ts\":\"\(ts)\",\"pairingId\":\"\(self.pairingId)\",\"role\":\"\(self.role)\"}")
            }
        }
    }

    private func startReceive() {
        receiveTask?.cancel()
        receiveTask = Task { [weak self] in
            guard let self else { return }
            var handshakeConfirmed = false
            while !Task.isCancelled, !self.stopped {
                guard let task = self.task, task.state == .running else { break }
                do {
                    let message = try await task.receive()
                    self.lastReceivedAt = Date()
                    if !handshakeConfirmed {
                        handshakeConfirmed = true
                        self.updateState(.connected)
                    }
                    switch message {
                    case .data(let data):
                        self.handle(data: data)
                    case .string(let text):
                        if let data = text.data(using: .utf8) {
                            self.handle(data: data)
                        }
                    @unknown default:
                        break
                    }
                } catch {
                    break
                }
            }
            if !self.stopped {
                self.updateState(.disconnected)
                self.scheduleReconnect()
            }
        }
    }

    // Watchdog: if no frame (or ping) is received within 45s, the socket is dead —
    // cancel it so the receive loop exits and we reconnect (M13).
    private func startWatchdog() {
        watchdogTask?.cancel()
        watchdogTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled, !self.stopped {
                try? await Task.sleep(nanoseconds: 5_000_000_000)
                if self.stopped { break }
                if Date().timeIntervalSince(self.lastReceivedAt) >= Self.receiveTimeout {
                    if let task = self.task, task.state == .running {
                        task.cancel(with: .abnormalClosure, reason: "receive timeout".data(using: .utf8))
                    }
                    break
                }
            }
        }
    }

    private func handle(data: Data) {
        do {
            let frame = try JSONDecoder().decode(CompanionRelayFrame.self, from: data)
            frameHandler?(frame)
        } catch {
            print("[companion] relay decode error: \(error.localizedDescription)")
        }
    }

    private func scheduleReconnect() {
        guard !stopped else { return }
        let delay = Self.backoff[min(backoffIndex, Self.backoff.count - 1)]
        backoffIndex = min(backoffIndex + 1, Self.backoff.count - 1)
        Task { [weak self] in
            guard let self else { return }
            try? await Task.sleep(nanoseconds: UInt64(delay * 1_000_000_000))
            guard !self.stopped else { return }
            self.connect()
        }
    }

    private func updateState(_ newState: CompanionRelayState) {
        state = newState
        stateHandler?(newState)
    }

    private static func isTerminalPairingFailure(_ message: String) -> Bool {
        let text = message.lowercased()
        return text.contains("revoked")
            || text.contains("not authorized")
            || text.contains("pairing not found")
            || text.contains("no longer available")
    }
}

enum CompanionRelayState { case connecting, connected, disconnected }

struct CompanionRelayFrame: Codable {
    var v: Int = 1
    var id: String?
    var type: String?
    // `ts` is kept as a String so an unexpected date format from the relay never breaks
    // frame decoding (the client does not interpret `ts`). (C2)
    var ts: String?
    var pairingId: String?
    var role: String?
    // Top-level fields used by relay.error (code/message are sent at the envelope top
    // level, not under body). (M18)
    var code: String?
    var message: String?
    var body: AnyCodable?

    func string(_ key: String) -> String? {
        body?.value(key) as? String
    }

    func bool(_ key: String) -> Bool? {
        body?.value(key) as? Bool
    }
}

/// Lightweight AnyCodable wrapper so the relay frame body can carry arbitrary JSON without
/// forcing a fixed schema on the desktop client.
struct AnyCodable: Codable {
    private let valueBox: Any

    init(_ value: Any) { valueBox = value }

    init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let dict = try? container.decode([String: AnyCodable].self) {
            // Lowercase dict keys so body field reads are case-insensitive (L3 hardening).
            // The phone sends camelCase today, but this protects against any casing drift.
            valueBox = Dictionary(uniqueKeysWithValues: dict.map { ($0.key.lowercased(), $0.value) })
        } else if let string = try? container.decode(String.self) {
            valueBox = string
        } else if let number = try? container.decode(Double.self) {
            valueBox = number
        } else if let bool = try? container.decode(Bool.self) {
            valueBox = bool
        } else {
            valueBox = NSNull()
        }
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        if let dict = valueBox as? [String: AnyCodable] {
            try container.encode(dict)
        } else if let string = valueBox as? String {
            try container.encode(string)
        } else if let number = valueBox as? Double {
            try container.encode(number)
        } else if let bool = valueBox as? Bool {
            try container.encode(bool)
        } else {
            try container.encodeNil()
        }
    }

    func value(_ key: String) -> Any? {
        if let dict = valueBox as? [String: AnyCodable], let entry = dict[key.lowercased()] {
            return entry.valueBox
        }
        return nil
    }
}
