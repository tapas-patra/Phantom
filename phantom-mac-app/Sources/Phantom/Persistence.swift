import AppKit
import CryptoKit
import Foundation
import Security

struct HostedConfiguration: Decodable {
    let websiteBaseUrl: String
    let desktopBackendBaseUrl: String

    static func load() -> HostedConfiguration {
        let environment = ProcessInfo.processInfo.environment
        let candidates = [
            Bundle.main.url(forResource: "phantom.hosted", withExtension: "json"),
            URL(fileURLWithPath: FileManager.default.currentDirectoryPath)
                .appendingPathComponent("phantom.hosted.json")
        ].compactMap { $0 }

        var fileConfiguration: HostedConfiguration?
        for url in candidates {
            if let data = try? Data(contentsOf: url),
               let value = try? JSONDecoder().decode(HostedConfiguration.self, from: data) {
                fileConfiguration = value
                break
            }
        }

        return HostedConfiguration(
            websiteBaseUrl: environment["PHANTOM_WEBSITE_BASE_URL"]
                ?? fileConfiguration?.websiteBaseUrl
                ?? "https://phantom-interview.vercel.app",
            desktopBackendBaseUrl: environment["PHANTOM_WINDOWS_BACKEND_BASE_URL"]
                ?? fileConfiguration?.desktopBackendBaseUrl
                ?? "https://phantom-ai-windows-app-backend.onrender.com"
        )
    }

    var validationError: String? {
        for (name, value) in [("website", websiteBaseUrl), ("desktop backend", desktopBackendBaseUrl)] {
            guard let url = URL(string: value),
                  let scheme = url.scheme?.lowercased(),
                  ["http", "https"].contains(scheme),
                  url.host != nil else {
                return "The Phantom \(name) URL is invalid: \(value)"
            }
        }
        return nil
    }
}

enum AppVersion {
    static var current: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String
            ?? Bundle.main.object(forInfoDictionaryKey: "CFBundleVersion") as? String
            ?? "development"
    }
}

struct DeviceIdentity {
    let installId: String
    let label: String
    let fingerprint: String
    let secretHint: String

    static func load() -> DeviceIdentity {
        let defaults = UserDefaults.standard
        let installId = defaults.string(forKey: "device.installId") ?? UUID().uuidString
        defaults.set(installId, forKey: "device.installId")

        let secret = Keychain.load("device.secret") ?? Data(UUID().uuidString.utf8)
        try? Keychain.save(secret, key: "device.secret")
        let label = Host.current().localizedName ?? "Mac"
        let fingerprint = sha256("\(installId)|\(label)|\(secret.base64EncodedString())")
        let secretHint = String(sha256(secret.base64EncodedString()).prefix(12))
        return DeviceIdentity(
            installId: installId,
            label: label,
            fingerprint: fingerprint,
            secretHint: secretHint
        )
    }

    private static func sha256(_ value: String) -> String {
        SHA256.hash(data: Data(value.utf8)).map { String(format: "%02x", $0) }.joined()
    }
}

enum AccountSnapshotStore {
    private static var url: URL? {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appendingPathComponent("Phantom", isDirectory: true)
            .appendingPathComponent("account-snapshot.json")
    }

    static func load() throws -> StartupSnapshot? {
        guard let url, FileManager.default.fileExists(atPath: url.path) else { return nil }
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return try decoder.decode(StartupSnapshot.self, from: Data(contentsOf: url))
    }

    static func save(_ snapshot: StartupSnapshot) throws {
        guard let url else { throw CocoaError(.fileNoSuchFile) }
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        let data = try encoder.encode(snapshot)
        try data.write(to: url, options: [.atomic, .completeFileProtection])
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }

    static func clear() {
        guard let url else { return }
        try? FileManager.default.removeItem(at: url)
    }
}

enum ManagedCatalogStore {
    private static var url: URL? {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appendingPathComponent("Phantom", isDirectory: true)
            .appendingPathComponent("managed-catalog.json")
    }

    static func load() -> ManagedCatalog? {
        guard let url, let data = try? Data(contentsOf: url) else { return nil }
        return try? JSONDecoder().decode(ManagedCatalog.self, from: data)
    }

    static func save(_ catalog: ManagedCatalog) throws {
        guard let url else { throw CocoaError(.fileNoSuchFile) }
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try JSONEncoder().encode(catalog).write(to: url, options: [.atomic, .completeFileProtection])
        try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
    }
}

enum BYOCatalogStore {
    static func models(provider: String) -> [ManagedModel]? {
        guard let data = UserDefaults.standard.data(forKey: "catalog.\(provider.lowercased()).models") else { return nil }
        return try? JSONDecoder().decode([ManagedModel].self, from: data)
    }

    static func isStale(provider: String, now: Date = Date()) -> Bool {
        guard let refreshed = UserDefaults.standard.object(forKey: "catalog.\(provider.lowercased()).refreshedAt") as? Date else { return true }
        return now.timeIntervalSince(refreshed) >= 12 * 60 * 60
    }

    static func save(provider: String, models: [ManagedModel], now: Date = Date()) {
        guard !models.isEmpty, let data = try? JSONEncoder().encode(models) else { return }
        UserDefaults.standard.set(data, forKey: "catalog.\(provider.lowercased()).models")
        UserDefaults.standard.set(now, forKey: "catalog.\(provider.lowercased()).refreshedAt")
    }
}

enum SessionStore {
    private static let key = "auth.session"

    static func load() -> AuthSession? {
        guard let data = Keychain.load(key) else { return nil }
        return try? JSONDecoder().decode(AuthSession.self, from: data)
    }

    static func save(_ session: AuthSession) throws {
        try Keychain.save(JSONEncoder().encode(session), key: key)
    }

    static func clear() {
        Keychain.delete(key)
    }
}

enum ConversationStore {
    private static var url: URL? {
        guard let root = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return nil }
        return root.appendingPathComponent("Phantom", isDirectory: true)
            .appendingPathComponent("conversation.json")
    }

    static func load() -> [ChatMessage] {
        guard let url, let data = try? Data(contentsOf: url) else { return [] }
        return (try? JSONDecoder().decode([ChatMessage].self, from: data))?
            .filter { !$0.content.isEmpty } ?? []
    }

    static func save(_ messages: [ChatMessage]) {
        guard let url, let data = try? JSONEncoder().encode(messages) else { return }
        try? FileManager.default.createDirectory(
            at: url.deletingLastPathComponent(),
            withIntermediateDirectories: true
        )
        do {
            try data.write(to: url, options: .atomic)
            try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
        } catch {
            return
        }
    }

    static func clear() {
        guard let url else { return }
        try? FileManager.default.removeItem(at: url)
    }
}

enum ContextSummaryStore {
    static func load(kind: String, source: String) -> String? {
        guard !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
              let data = Keychain.load(key(kind: kind, source: source)) else { return nil }
        return String(data: data, encoding: .utf8)
    }

    static func save(kind: String, source: String, summary: String) throws {
        try Keychain.save(Data(summary.utf8), key: key(kind: kind, source: source))
    }

    static func summarize(kind: String, source: String) -> String {
        let words = source.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        if kind == "resume" {
            let lines = source.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }.filter { !$0.isEmpty }
            let name = lines.first ?? "Not provided"
            let role = lines.first(where: { line in ["engineer", "developer", "manager", "analyst", "architect", "designer", "consultant"].contains(where: line.lowercased().contains) }) ?? "Not explicitly stated"
            return "**Name:** \(String(name.prefix(100)))\n**Role & Experience:** \(String(role.prefix(220)))\n**Core Skills / Domain / Strengths:** \(words.prefix(110).joined(separator: " "))"
        }
        return words.prefix(100).joined(separator: " ")
    }

    private static func key(kind: String, source: String) -> String {
        let hash = SHA256.hash(data: Data(source.utf8)).map { String(format: "%02x", $0) }.joined()
        return "context.summary.\(kind).\(hash)"
    }
}

enum Diagnostics {
    private static let maxLogBytes = 1_000_000
    private static let maxStructuredBytes = 5 * 1_024 * 1_024
    private static let maxStructuredFiles = 5
    private static let writer = DispatchQueue(label: "phantom.diagnostics.writer", qos: .utility)
    private static var terminalTurns = Set<String>()
    private static let liveFields = Set([
        "outcome", "error_code", "question_type", "intent", "action", "answer_basis", "confidence_bucket",
        "entity_type", "has_entity_id", "protocol_version", "prefix_bytes", "validation_outcome",
        "estimated_input_tokens", "max_output_tokens", "recent_turn_count", "snippet_count", "image_present",
        "status_class", "search_mode", "cache_hit", "candidate_count", "transcript_length_bucket",
        "duplicate_suppression_count", "chunk_count", "buffered_characters", "flush_count", "render_ms",
        "retrieval_status", "provider", "model", "model_call", "attempt", "elapsed_ms", "stage"
    ])
    private static var root: URL? {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first?
            .appendingPathComponent("Phantom", isDirectory: true)
    }
    private static var logURL: URL? { root?.appendingPathComponent("phantom.log") }
    private static var structuredURL: URL? { root?.appendingPathComponent("live-copilot.jsonl") }
    private static var crashURL: URL? { root?.appendingPathComponent("last-crash.txt") }

    static func install() {
        NSSetUncaughtExceptionHandler(phantomExceptionHandler)
        log("app_started version=\(AppVersion.current)")
    }

    static func log(_ message: String) {
        guard let root, let logURL else { return }
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        let line = "\(ISO8601DateFormatter().string(from: Date())) \(message)\n"
        if !FileManager.default.fileExists(atPath: logURL.path) { FileManager.default.createFile(atPath: logURL.path, contents: nil) }
        guard let handle = try? FileHandle(forWritingTo: logURL) else { return }
        defer { try? handle.close() }
        _ = try? handle.seekToEnd()
        try? handle.write(contentsOf: Data(line.utf8))
        trimIfNeeded(logURL)
    }

    static func event(
        _ event: String,
        level: String = "Information",
        sessionId: String,
        turnId: String,
        operationId: String = "",
        mode: CopilotMode,
        style: InterviewDeliveryStyle,
        fields: [String: String] = [:]
    ) {
        let safeFields = sanitizedFields(fields)
        writer.async {
            let terminal = ["answer_completed", "turn_cancelled", "turn_failed"].contains(event)
            if terminal, !self.terminalTurns.insert(turnId).inserted { return }
            guard let root, let url = self.structuredURL else { return }
            var envelope: [String: Any] = [
                "timestamp_utc": ISO8601DateFormatter().string(from: Date()),
                "level": level,
                "service": "phantom-mac-desktop",
                "component": "live_copilot",
                "event": event,
                "session_id": sessionId,
                "turn_id": turnId,
                "operation_id": operationId,
                "mode": mode.rawValue,
                "delivery_style": style.rawValue
            ]
            safeFields.forEach { envelope[$0.key] = $0.value }
            guard JSONSerialization.isValidJSONObject(envelope),
                  let data = try? JSONSerialization.data(withJSONObject: envelope),
                  var line = String(data: data, encoding: .utf8) else { return }
            line += "\n"
            try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
            self.rotateStructuredIfNeeded(url)
            if !FileManager.default.fileExists(atPath: url.path) { FileManager.default.createFile(atPath: url.path, contents: nil) }
            guard let handle = try? FileHandle(forWritingTo: url) else { return }
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: Data(line.utf8))
            if event == "session_ended" { self.terminalTurns.remove(turnId) }
        }
    }

    static func sanitizedFields(_ fields: [String: String]) -> [String: String] {
        Dictionary(uniqueKeysWithValues: fields.compactMap { key, value in
            guard liveFields.contains(key), value.count <= 160 else { return nil }
            return (key, value)
        })
    }

    static func recordCrash(_ message: String) {
        log("unhandled_exception \(message)")
        guard let root, let crashURL else { return }
        try? FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try? Data(message.utf8).write(to: crashURL, options: .atomic)
    }

    static func consumeCrash() -> String? {
        guard let crashURL, let data = try? Data(contentsOf: crashURL) else { return nil }
        try? FileManager.default.removeItem(at: crashURL)
        return String(data: data, encoding: .utf8)
    }

    static func text() -> String {
        let legacy = logURL.flatMap { try? Data(contentsOf: $0) }.flatMap { String(data: $0, encoding: .utf8) }
        let structured = structuredURL.flatMap { try? Data(contentsOf: $0) }.flatMap { String(data: $0, encoding: .utf8) }
        let sections = [
            legacy.map { String($0.suffix(30_000)) },
            structured.map { "Live Copilot structured events (content-free):\n" + String($0.suffix(30_000)) }
        ].compactMap { $0 }
        return sections.isEmpty ? "No diagnostics recorded." : sections.joined(separator: "\n\n")
    }

    static func clear() {
        if let logURL { try? FileManager.default.removeItem(at: logURL) }
        if let structuredURL { try? FileManager.default.removeItem(at: structuredURL) }
    }

    private static func trimIfNeeded(_ logURL: URL) {
        guard let values = try? logURL.resourceValues(forKeys: [.fileSizeKey]),
              (values.fileSize ?? 0) > maxLogBytes,
              let data = try? Data(contentsOf: logURL) else { return }
        try? data.suffix(maxLogBytes / 2).write(to: logURL, options: .atomic)
    }

    private static func rotateStructuredIfNeeded(_ url: URL) {
        guard let size = try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize,
              size >= maxStructuredBytes else { return }
        let oldest = URL(fileURLWithPath: url.path + ".\(maxStructuredFiles - 1)")
        try? FileManager.default.removeItem(at: oldest)
        if maxStructuredFiles > 2 {
            for index in stride(from: maxStructuredFiles - 2, through: 1, by: -1) {
                let source = URL(fileURLWithPath: url.path + ".\(index)")
                let destination = URL(fileURLWithPath: url.path + ".\(index + 1)")
                if FileManager.default.fileExists(atPath: source.path) {
                    try? FileManager.default.moveItem(at: source, to: destination)
                }
            }
        }
        try? FileManager.default.moveItem(at: url, to: URL(fileURLWithPath: url.path + ".1"))
    }
}

private func phantomExceptionHandler(_ exception: NSException) {
    Diagnostics.recordCrash(exception.reason ?? exception.name.rawValue)
}

enum Keychain {
    private static let service = "com.phantom.desktop.mac"

    static func load(_ key: String) -> Data? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess else { return nil }
        return result as? Data
    }

    static func save(_ data: Data, key: String) throws {
        delete(key)
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else { throw KeychainError.status(status) }
    }

    static func delete(_ key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        SecItemDelete(query as CFDictionary)
    }
}

enum KeychainError: LocalizedError {
    case status(OSStatus)

    var errorDescription: String? {
        switch self {
        case .status(let status):
            return SecCopyErrorMessageString(status, nil) as String? ?? "Keychain error \(status)."
        }
    }
}
