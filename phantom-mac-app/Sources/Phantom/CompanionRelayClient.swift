import Foundation

/// WebSocket relay client for Companion Mode on macOS. Connects to the hosted relay with a
/// short-lived ticket using URLSessionWebSocketTask, sends/receives JSON envelopes, and
/// reconnects with a capped backoff. Inbound frames are surfaced via `frameHandler`.
@MainActor
final class CompanionRelayClient {
    private static let subprotocol = "phantom.companion.v1"
    private static let receiveTimeout: TimeInterval = 45
    private static let backoff: [TimeInterval] = [1, 2, 4, 8, 15]

    private let relayUrl: URL
    private let ticket: String
    private var task: URLSessionWebSocketTask?
    private var pingTask: Task<Void, Never>?
    private var receiveTask: Task<Void, Never>?
    private var backoffIndex = 0
    private var stopped = false

    let pairingId: String
    let role: String
    private(set) var state: CompanionRelayState = .disconnected

    var frameHandler: ((CompanionRelayFrame) -> Void)?
    var stateHandler: ((CompanionRelayState) -> Void)?

    init(relayUrl: URL, ticket: String, pairingId: String, role: String) {
        self.relayUrl = relayUrl
        self.ticket = ticket
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
        task?.cancel(with: .goingAway, reason: nil)
        task = nil
        updateState(.disconnected)
    }

    func send(frame: CompanionRelayFrame) {
        guard let task, task.state == .running else { return }
        do {
            let data = try JSONEncoder().encode(frame)
            task.send(.data(data)) { [weak self] error in
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
        var components = URLComponents(url: relayUrl, resolvingAgainstBaseURL: false)
        var queryItems = components?.queryItems ?? []
        queryItems.append(URLQueryItem(name: "ticket", value: ticket))
        components?.queryItems = queryItems
        guard let url = components?.url else {
            print("[companion] bad relay url")
            updateState(.disconnected)
            scheduleReconnect()
            return
        }
        let session = URLSession(configuration: .default)
        let wsTask = session.webSocketTask(with: url, protocols: [Self.subprotocol])
        task = wsTask
        wsTask.resume()
        backoffIndex = 0
        updateState(.connected)
        startPing()
        startReceive()
    }

    private func startPing() {
        pingTask?.cancel()
        pingTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled, !self.stopped {
                try? await Task.sleep(nanoseconds: 20_000_000_000)
                guard let task = self.task, task.state == .running else { break }
                task.sendPing { _ in }
            }
        }
    }

    private func startReceive() {
        receiveTask?.cancel()
        receiveTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled, !self.stopped {
                guard let task = self.task, task.state == .running else { break }
                do {
                    let message = try await task.receive()
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
}

enum CompanionRelayState { case connecting, connected, disconnected }

struct CompanionRelayFrame: Codable {
    var v: Int = 1
    var id: String?
    var type: String?
    var ts: Date?
    var pairingId: String?
    var role: String?
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
            valueBox = dict
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
        if let dict = valueBox as? [String: AnyCodable], let entry = dict[key] {
            return entry.valueBox
        }
        return nil
    }
}
