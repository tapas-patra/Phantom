import Foundation

actor RuntimePersistence {
    private var state: RuntimeState
    private let url: URL
    nonisolated let bootstrapFailure: String?

    init() {
        let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Phantom", isDirectory: true)
        url = root.appendingPathComponent("runtime-state.json")
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        if FileManager.default.fileExists(atPath: url.path) {
            do {
                state = try decoder.decode(RuntimeState.self, from: Data(contentsOf: url))
                bootstrapFailure = nil
            } catch {
                state = RuntimeState()
                bootstrapFailure = "Runtime storage could not be opened safely: \(error.localizedDescription)"
            }
        } else {
            state = RuntimeState()
            bootstrapFailure = nil
        }
    }

    func snapshot() -> RuntimeState { state }

    func setSession(_ session: InterviewSession?) throws {
        state.activeSession = session
        try save()
    }

    func setWallet(_ wallet: RuntimeWallet) throws {
        state.wallet = wallet
        try save()
    }

    func enqueue(_ payload: UsageReconciliationPayload) throws {
        state.reconciliations.append(UsageReconciliationRecord(
            recordId: "usage-\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())",
            payload: payload
        ))
        try save()
    }

    func updateReconciliation(_ record: UsageReconciliationRecord) throws {
        guard let index = state.reconciliations.firstIndex(where: { $0.recordId == record.recordId }) else { return }
        state.reconciliations[index] = record
        try save()
    }

    func pendingReconciliations() -> [UsageReconciliationRecord] {
        state.reconciliations.filter { $0.status == .pending || $0.status == .failed }
    }

    func appendTelemetry(_ event: PhantomTelemetryEvent) throws {
        state.telemetry.append(event)
        if state.telemetry.count > 500 { state.telemetry.removeFirst(state.telemetry.count - 500) }
        try save()
    }

    func pendingTelemetry() -> [PhantomTelemetryEvent] { state.telemetry }

    func diagnostics() -> RuntimeDiagnostics {
        RuntimeDiagnostics(
            pendingUsage: state.reconciliations.filter { $0.status == .pending }.count,
            failedUsage: state.reconciliations.filter { $0.status == .failed }.count,
            deadLetters: Array(state.reconciliations.filter { $0.status == .deadLetter }.suffix(10)),
            queuedTelemetry: state.telemetry.count
        )
    }

    func removeTelemetry(ids: Set<String>) throws {
        state.telemetry.removeAll { ids.contains($0.eventId) }
        try save()
    }

    private func save() throws {
        if let bootstrapFailure { throw RuntimePersistenceError.readOnly(bootstrapFailure) }
        try FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(state)
        try data.write(to: url, options: [.atomic, .completeFileProtection])
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}

enum RuntimePersistenceError: LocalizedError {
    case readOnly(String)
    var errorDescription: String? {
        if case .readOnly(let message) = self { return message }
        return nil
    }
}
