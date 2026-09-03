import Foundation

struct AuthSession: Codable {
    let userId: String
    let email: String
    let accessToken: String
    let refreshToken: String
    let authMethod: String
    let deviceInstallId: String
    let deviceFingerprintHash: String
    let authenticatedAtUtc: Date?
    let expiresAtUtc: Date?
    let isAuthenticated: Bool
}

struct StartupSnapshot: Codable {
    struct Wallet: Codable {
        var proAvailableCredits: Decimal
        var premiumAvailableCredits: Decimal
        var premiumNegativeCredits: Decimal
    }

    struct KnowledgeBase: Codable {
        let knowledgeBaseId: String
        let name: String
        let description: String
        let status: String
        let embeddingModel: String
        let embeddingVersion: Int
        let documentCount: Int
        let chunkCount: Int
        let canUseInInterview: Bool
        let blockedReason: String
        let lastProcessedAtUtc: Date?
        let profileCard: KnowledgeProfile?
        let experienceCards: [KnowledgeExperience]?
        let projectCards: [KnowledgeProject]?
    }

    let userId: String
    let email: String
    let emailVerified: Bool
    let accessTier: String
    var wallet: Wallet
    let leaseExpiresAtUtc: Date?
    let hasResumableLockedSession: Bool
    let lastLockTokenHash: String
    let lastLockedSessionId: String
    let offlineModeEnabled: Bool
    let canUseDesktopPowerFeatures: Bool
    let lastValidatedAtUtc: Date
    let hostedKnowledgeBase: KnowledgeBase
    let source: String

    var availableCredits: Decimal {
        wallet.proAvailableCredits + wallet.premiumAvailableCredits
    }

    var runtimeWallet: RuntimeWallet {
        RuntimeWallet(
            proAvailableCredits: wallet.proAvailableCredits,
            premiumAvailableCredits: wallet.premiumAvailableCredits,
            premiumNegativeCredits: wallet.premiumNegativeCredits
        )
    }
}

enum StartupGateState: String {
    case ready
    case restricted
    case offline
    case backendUnavailable
    case verificationRequired
}

struct AppLaunchContext {
    let state: StartupGateState
    let title: String
    let message: String
    let canOpenApp: Bool
    let canStartInterview: Bool
    let canResumeLockedInterview: Bool

    static let checking = AppLaunchContext(
        state: .backendUnavailable,
        title: "Checking Account",
        message: "Validating your account and interview lock.",
        canOpenApp: false,
        canStartInterview: false,
        canResumeLockedInterview: false
    )

    static func evaluate(_ snapshot: StartupSnapshot, offline: Bool, now: Date = Date()) -> AppLaunchContext {
        guard snapshot.emailVerified else {
            return AppLaunchContext(state: .verificationRequired, title: "Email Verification Required", message: "Verify your email on the Phantom website, then retry.", canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
        }
        let resumable = snapshot.hasResumableLockedSession
        if snapshot.offlineModeEnabled, let expiry = snapshot.leaseExpiresAtUtc, expiry <= now {
            return AppLaunchContext(state: .restricted, title: "Offline Lease Expired", message: resumable ? "You may resume the locked interview, but new interviews are blocked until account validation succeeds." : "Reconnect and retry before starting an interview.", canOpenApp: resumable, canStartInterview: false, canResumeLockedInterview: resumable)
        }
        if offline, !resumable, !(snapshot.leaseExpiresAtUtc.map { $0 > now } ?? false) {
            return AppLaunchContext(state: .backendUnavailable, title: "Backend Unavailable", message: "Offline launch requires a valid cached lease or resumable interview.", canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
        }
        if snapshot.wallet.premiumNegativeCredits > 0 {
            return AppLaunchContext(state: .restricted, title: "Negative Premium Balance", message: "The app is available, but a new interview is blocked until the balance is cleared.", canOpenApp: true, canStartInterview: false, canResumeLockedInterview: resumable)
        }
        let credits = snapshot.accessTier.lowercased() == "free"
            ? snapshot.wallet.premiumAvailableCredits
            : snapshot.availableCredits
        if credits <= 0 {
            return AppLaunchContext(state: .restricted, title: "No Credits Available", message: resumable ? "Resume the locked interview or add credits before starting another." : "Add credits before starting an interview.", canOpenApp: true, canStartInterview: false, canResumeLockedInterview: resumable)
        }
        return AppLaunchContext(state: offline ? .offline : .ready, title: offline ? "Offline Mode" : "Ready", message: offline ? "Using cached account validation until the backend reconnects." : "Account validation passed.", canOpenApp: true, canStartInterview: true, canResumeLockedInterview: resumable)
    }
}

struct KnowledgeProfile: Codable {
    let profileCardId: String
    let fullName: String
    let resumeText: String
    let candidateInfo: String
    let shortIntro: String
    let currentRole: String
    let yearsOfExperience: Int
    let strengths: [String]
    let skills: [String]
    let domains: [String]
    let sourceDocumentIds: [String]
    let updatedAtUtc: Date
}

struct KnowledgeExperience: Codable {
    let experienceCardId: String
    let company: String
    let role: String
    let isCurrent: Bool
    let sortOrder: Int
    let startDate: String
    let endDate: String
    let summary: String
    let responsibilities: String
    let skills: [String]
    let sourceDocumentIds: [String]
    let updatedAtUtc: Date
}

struct KnowledgeProject: Codable {
    let projectCardId: String
    let title: String
    let slug: String
    let isRecent: Bool
    let sortOrder: Int
    let role: String
    let summary: String
    let stack: [String]
    let architecture: String
    let challenges: String
    let impact: String
    let sourceDocumentIds: [String]
    let updatedAtUtc: Date
}

struct ManagedCatalog: Codable {
    let providers: [ManagedProvider]
}

struct ManagedProvider: Codable, Identifiable, Hashable {
    let providerId: String
    let label: String
    let models: [ManagedModel]
    var id: String { providerId }
}

struct ManagedModel: Codable, Identifiable, Hashable {
    let modelId: String
    let displayName: String
    let supportsVision: Bool
    var id: String { modelId }
}

struct ContextPack: Codable, Identifiable, Hashable {
    let packId: String
    let name: String
    let resumeText: String
    let jobDescriptionText: String
    let updatedAtUtc: Date
    var id: String { packId }
}

struct KnowledgeSnippet: Codable {
    let documentId: String
    let documentTitle: String
    let text: String
    let score: Double
}

private extension KeyedDecodingContainer {
    func value<T: Decodable>(_ type: T.Type, forKey key: Key, default fallback: T) -> T {
        (try? decode(type, forKey: key)) ?? fallback
    }

    func optional<T: Decodable>(_ type: T.Type, forKey key: Key) -> T? {
        try? decodeIfPresent(type, forKey: key)
    }
}

extension StartupSnapshot {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        userId = values.value(String.self, forKey: .userId, default: "")
        email = try values.decode(String.self, forKey: .email)
        emailVerified = try values.decode(Bool.self, forKey: .emailVerified)
        accessTier = try values.decode(String.self, forKey: .accessTier)
        wallet = try values.decode(Wallet.self, forKey: .wallet)
        leaseExpiresAtUtc = values.optional(Date.self, forKey: .leaseExpiresAtUtc)
        hasResumableLockedSession = values.value(Bool.self, forKey: .hasResumableLockedSession, default: false)
        lastLockTokenHash = values.value(String.self, forKey: .lastLockTokenHash, default: "")
        lastLockedSessionId = values.value(String.self, forKey: .lastLockedSessionId, default: "")
        offlineModeEnabled = values.value(Bool.self, forKey: .offlineModeEnabled, default: false)
        canUseDesktopPowerFeatures = values.value(Bool.self, forKey: .canUseDesktopPowerFeatures, default: false)
        lastValidatedAtUtc = values.value(Date.self, forKey: .lastValidatedAtUtc, default: Date())
        hostedKnowledgeBase = values.value(KnowledgeBase.self, forKey: .hostedKnowledgeBase, default: .empty)
        source = values.value(String.self, forKey: .source, default: "hosted")
    }
}

extension StartupSnapshot.KnowledgeBase {
    static let empty = Self(
        knowledgeBaseId: "", name: "", description: "", status: "not_created",
        embeddingModel: "", embeddingVersion: 0, documentCount: 0, chunkCount: 0,
        canUseInInterview: false, blockedReason: "", lastProcessedAtUtc: nil,
        profileCard: nil, experienceCards: [], projectCards: []
    )

    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        knowledgeBaseId = values.value(String.self, forKey: .knowledgeBaseId, default: "")
        name = values.value(String.self, forKey: .name, default: "")
        description = values.value(String.self, forKey: .description, default: "")
        status = values.value(String.self, forKey: .status, default: "not_created")
        embeddingModel = values.value(String.self, forKey: .embeddingModel, default: "")
        embeddingVersion = values.value(Int.self, forKey: .embeddingVersion, default: 0)
        documentCount = values.value(Int.self, forKey: .documentCount, default: 0)
        chunkCount = values.value(Int.self, forKey: .chunkCount, default: 0)
        canUseInInterview = values.value(Bool.self, forKey: .canUseInInterview, default: false)
        blockedReason = values.value(String.self, forKey: .blockedReason, default: "")
        lastProcessedAtUtc = values.optional(Date.self, forKey: .lastProcessedAtUtc)
        profileCard = values.optional(KnowledgeProfile.self, forKey: .profileCard)
        experienceCards = values.value([KnowledgeExperience].self, forKey: .experienceCards, default: [])
        projectCards = values.value([KnowledgeProject].self, forKey: .projectCards, default: [])
    }
}

extension KnowledgeProfile {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        profileCardId = values.value(String.self, forKey: .profileCardId, default: "")
        fullName = values.value(String.self, forKey: .fullName, default: "")
        resumeText = values.value(String.self, forKey: .resumeText, default: "")
        candidateInfo = values.value(String.self, forKey: .candidateInfo, default: "")
        shortIntro = values.value(String.self, forKey: .shortIntro, default: "")
        currentRole = values.value(String.self, forKey: .currentRole, default: "")
        yearsOfExperience = values.value(Int.self, forKey: .yearsOfExperience, default: 0)
        strengths = values.value([String].self, forKey: .strengths, default: [])
        skills = values.value([String].self, forKey: .skills, default: [])
        domains = values.value([String].self, forKey: .domains, default: [])
        sourceDocumentIds = values.value([String].self, forKey: .sourceDocumentIds, default: [])
        updatedAtUtc = values.value(Date.self, forKey: .updatedAtUtc, default: .distantPast)
    }
}

extension KnowledgeExperience {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        experienceCardId = values.value(String.self, forKey: .experienceCardId, default: "")
        company = values.value(String.self, forKey: .company, default: "")
        role = values.value(String.self, forKey: .role, default: "")
        isCurrent = values.value(Bool.self, forKey: .isCurrent, default: false)
        sortOrder = values.value(Int.self, forKey: .sortOrder, default: 0)
        startDate = values.value(String.self, forKey: .startDate, default: "")
        endDate = values.value(String.self, forKey: .endDate, default: "")
        summary = values.value(String.self, forKey: .summary, default: "")
        responsibilities = values.value(String.self, forKey: .responsibilities, default: "")
        skills = values.value([String].self, forKey: .skills, default: [])
        sourceDocumentIds = values.value([String].self, forKey: .sourceDocumentIds, default: [])
        updatedAtUtc = values.value(Date.self, forKey: .updatedAtUtc, default: .distantPast)
    }
}

extension KnowledgeProject {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        projectCardId = values.value(String.self, forKey: .projectCardId, default: "")
        title = values.value(String.self, forKey: .title, default: "")
        slug = values.value(String.self, forKey: .slug, default: "")
        isRecent = values.value(Bool.self, forKey: .isRecent, default: false)
        sortOrder = values.value(Int.self, forKey: .sortOrder, default: 0)
        role = values.value(String.self, forKey: .role, default: "")
        summary = values.value(String.self, forKey: .summary, default: "")
        stack = values.value([String].self, forKey: .stack, default: [])
        architecture = values.value(String.self, forKey: .architecture, default: "")
        challenges = values.value(String.self, forKey: .challenges, default: "")
        impact = values.value(String.self, forKey: .impact, default: "")
        sourceDocumentIds = values.value([String].self, forKey: .sourceDocumentIds, default: [])
        updatedAtUtc = values.value(Date.self, forKey: .updatedAtUtc, default: .distantPast)
    }
}

extension ContextPack {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        packId = try values.decode(String.self, forKey: .packId)
        name = try values.decode(String.self, forKey: .name)
        resumeText = values.value(String.self, forKey: .resumeText, default: "")
        jobDescriptionText = values.value(String.self, forKey: .jobDescriptionText, default: "")
        updatedAtUtc = values.value(Date.self, forKey: .updatedAtUtc, default: .distantPast)
    }
}

extension KnowledgeSnippet {
    init(from decoder: Decoder) throws {
        let values = try decoder.container(keyedBy: CodingKeys.self)
        documentId = values.value(String.self, forKey: .documentId, default: "")
        documentTitle = values.value(String.self, forKey: .documentTitle, default: "Knowledge Base")
        text = try values.decode(String.self, forKey: .text)
        score = values.value(Double.self, forKey: .score, default: 0)
    }
}

private struct KnowledgeSearchResult: Decodable {
    let snippets: [KnowledgeSnippet]
}

struct ClarificationOption: Codable, Equatable {
    let label: String
    let question: String
}

struct ChatMessage: Codable, Identifiable, Equatable {
    let id: UUID
    let role: String
    var content: String
    var summary: String?
    var createdAtUtc: Date?
    var estimatedTokens: Int?
    var hasCode: Bool?
    var answerSource: String?
    var interviewIntent: String?
    var clarificationOptions: [ClarificationOption]?
    var responseTimeMs: Int?

    init(id: UUID = UUID(), role: String, content: String, summary: String? = nil, createdAtUtc: Date = Date(), estimatedTokens: Int? = nil, hasCode: Bool? = nil, answerSource: String? = nil, interviewIntent: String? = nil, clarificationOptions: [ClarificationOption]? = nil, responseTimeMs: Int? = nil) {
        self.id = id
        self.role = role
        self.content = content
        self.summary = summary
        self.createdAtUtc = createdAtUtc
        self.estimatedTokens = estimatedTokens ?? max(1, content.count / 4)
        self.hasCode = hasCode ?? content.contains("```")
        self.answerSource = answerSource
        self.interviewIntent = interviewIntent
        self.clarificationOptions = clarificationOptions
        self.responseTimeMs = responseTimeMs
    }

    var responseTimeText: String? {
        guard let responseTimeMs else { return nil }
        return responseTimeMs < 1_000 ? "\(responseTimeMs) ms" : String(format: "%.1f s", Double(responseTimeMs) / 1_000)
    }
}

enum SSEEvent: Equatable {
    case delta(String)
    case failure(String)
    case done
}

enum SSEParser {
    static func parse(_ line: String) -> SSEEvent? {
        guard line.hasPrefix("data: ") else { return nil }
        let payload = String(line.dropFirst(6))
        if payload == "[DONE]" { return .done }
        guard let data = payload.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        if let error = object["error"] as? String { return .failure(error) }
        if let delta = object["delta"] as? String { return .delta(delta) }
        return nil
    }
}

struct BackendClient {
    let baseURL: URL
    private let decoder: JSONDecoder = {
        let value = JSONDecoder()
        value.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let text = try container.decode(String.self)
            let fractional = ISO8601DateFormatter()
            fractional.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
            let regular = ISO8601DateFormatter()
            guard let date = fractional.date(from: text) ?? regular.date(from: text) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Invalid ISO-8601 date: \(text)")
            }
            return date
        }
        return value
    }()
    private let encoder: JSONEncoder = {
        let value = JSONEncoder()
        value.dateEncodingStrategy = .iso8601
        return value
    }()

    func login(email: String, password: String, device: DeviceIdentity) async throws -> AuthSession {
        struct Body: Encodable {
            let email: String
            let password: String
            let appVersion: String
            let installId: String
            let deviceLabel: String
            let deviceFingerprintHash: String
            let secretFingerprintHint: String
        }
        return try await post(
            "/api/desktop/auth/login",
            body: Body(
                email: email,
                password: password,
                appVersion: AppVersion.current,
                installId: device.installId,
                deviceLabel: device.label,
                deviceFingerprintHash: device.fingerprint,
                secretFingerprintHint: device.secretHint
            )
        )
    }

    func refresh(_ session: AuthSession, device: DeviceIdentity) async throws -> AuthSession {
        struct Body: Encodable {
            let refreshToken: String
            let installId: String
            let deviceFingerprintHash: String
        }
        return try await post(
            "/api/desktop/auth/refresh",
            body: Body(
                refreshToken: session.refreshToken,
                installId: device.installId,
                deviceFingerprintHash: device.fingerprint
            )
        )
    }

    func startupCheck(_ session: AuthSession) async throws -> StartupSnapshot {
        try await post(
            "/api/desktop/account/startup-check/session",
            body: session,
            bearer: session.accessToken
        )
    }

    func catalog(accessToken: String) async throws -> ManagedCatalog {
        var request = URLRequest(url: url("/api/desktop/ai/catalog"))
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        return try await send(request)
    }

    func logout(_ session: AuthSession) async throws {
        struct Body: Encodable { let refreshToken: String }
        let _: LogoutResult = try await post(
            "/api/desktop/auth/logout",
            body: Body(refreshToken: session.refreshToken)
        )
    }

    func contextPacks(accessToken: String) async throws -> [ContextPack] {
        var request = URLRequest(url: url("/api/desktop/context-packs"))
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        return try await send(request)
    }

    func saveContextPack(
        accessToken: String,
        packId: String,
        name: String,
        resumeText: String,
        jobDescriptionText: String
    ) async throws -> ContextPack {
        struct Body: Encodable {
            let packId: String
            let name: String
            let resumeText: String
            let jobDescriptionText: String
        }
        return try await post(
            "/api/desktop/context-packs",
            body: Body(
                packId: packId,
                name: name,
                resumeText: resumeText,
                jobDescriptionText: jobDescriptionText
            ),
            bearer: accessToken
        )
    }

    func deleteContextPack(accessToken: String, packId: String) async throws {
        struct Body: Encodable { let packId: String }
        let _: DeleteResult = try await post(
            "/api/desktop/context-packs/delete",
            body: Body(packId: packId),
            bearer: accessToken
        )
    }

    func knowledgeSnippets(
        accessToken: String,
        query: String,
        preferredDocumentIds: [String] = [],
        turnId: String,
        operationId: String
    ) async throws -> [KnowledgeSnippet] {
        var components = URLComponents(
            url: url("/api/desktop/kb/search"),
            resolvingAgainstBaseURL: false
        )!
        components.queryItems = [
            URLQueryItem(name: "query", value: query),
            URLQueryItem(name: "maxSnippets", value: "3"),
            URLQueryItem(name: "preferredDocumentIds", value: preferredDocumentIds.prefix(8).joined(separator: ","))
        ]
        var request = URLRequest(url: components.url!)
        request.timeoutInterval = 1.5
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        correlate(&request, turnId: turnId, operationId: operationId)
        let result: KnowledgeSearchResult = try await send(request)
        return result.snippets
    }

    func knowledgeBase(accessToken: String) async throws -> StartupSnapshot.KnowledgeBase {
        var request = URLRequest(url: url("/api/desktop/kb"))
        request.timeoutInterval = 0.5
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        return try await send(request)
    }

    func acquireLock(
        accessToken: String,
        userId: String,
        deviceId: String,
        sessionId: String
    ) async throws -> DeviceLockResult {
        struct Body: Encodable {
            let userId: String
            let deviceId: String
            let sessionId: String
            let appVersion: String
        }
        return try await post(
            "/api/desktop/locks/acquire",
            body: Body(userId: userId, deviceId: deviceId, sessionId: sessionId, appVersion: AppVersion.current),
            bearer: accessToken
        )
    }

    func heartbeatLock(
        accessToken: String,
        sessionId: String,
        lockToken: String,
        deviceId: String
    ) async throws -> DeviceLockResult {
        struct Body: Encodable { let sessionId: String; let lockToken: String; let deviceId: String }
        return try await post(
            "/api/desktop/locks/heartbeat",
            body: Body(sessionId: sessionId, lockToken: lockToken, deviceId: deviceId),
            bearer: accessToken
        )
    }

    func releaseLock(accessToken: String, sessionId: String, lockToken: String) async throws {
        struct Body: Encodable { let sessionId: String; let lockToken: String; let releaseReason: String }
        let _: EmptyResult = try await post(
            "/api/desktop/locks/release",
            body: Body(sessionId: sessionId, lockToken: lockToken, releaseReason: "desktop_cleanup"),
            bearer: accessToken
        )
    }

    func reconcileUsage(accessToken: String, payload: UsageReconciliationPayload) async throws -> UsageReconciliationResult {
        try await post("/api/desktop/usage/reconcile", body: payload, bearer: accessToken)
    }

    func ingestTelemetry(accessToken: String, event: PhantomTelemetryEvent) async throws {
        struct Body: Encodable {
            let category: String
            let eventName: String
            let attributes: [String: String]
            let occurredAtUtc: Date
        }
        var request = URLRequest(url: url("/api/desktop/telemetry/ingest"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(accessToken)", forHTTPHeaderField: "Authorization")
        correlate(
            &request,
            turnId: event.attributes["turn_id"] ?? UUID().uuidString,
            operationId: event.attributes["operation_id"] ?? UUID().uuidString
        )
        request.httpBody = try encoder.encode(Body(
            category: event.category,
            eventName: event.eventName,
            attributes: event.attributes,
            occurredAtUtc: event.occurredAtUtc
        ))
        let _: EmptyResult = try await send(request)
    }

    func chatStream(
        session: AuthSession,
        provider: String,
        model: String,
        allowPaidSessionExtension: Bool,
        imageBase64: String?,
        messages: [ChatMessage],
        turnId: String,
        operationId: String
    ) async throws -> URLSession.AsyncBytes {
        struct WireMessage: Encodable {
            let role: String
            let content: String
        }
        struct Body: Encodable {
            let requestId: String
            let turnId: String
            let provider: String
            let model: String
            let allowPaidSessionExtension: Bool
            let imageBase64: String?
            let messages: [WireMessage]
        }

        var request = URLRequest(url: url("/api/desktop/ai/chat"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(session.accessToken)", forHTTPHeaderField: "Authorization")
        correlate(&request, turnId: turnId, operationId: operationId)
        request.timeoutInterval = 300
        request.httpBody = try encoder.encode(Body(
            requestId: operationId,
            turnId: turnId,
            provider: provider,
            model: model,
            allowPaidSessionExtension: allowPaidSessionExtension,
            imageBase64: imageBase64,
            messages: messages.map { WireMessage(role: $0.role, content: $0.content) }
        ))

        let (bytes, response) = try await URLSession.shared.bytes(for: request)
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode) else {
            throw BackendError.http((response as? HTTPURLResponse)?.statusCode ?? 0, "The managed AI request failed.")
        }
        return bytes
    }

    private func post<Body: Encodable, Result: Decodable>(
        _ path: String,
        body: Body,
        bearer: String? = nil
    ) async throws -> Result {
        var request = URLRequest(url: url(path))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let bearer { request.setValue("Bearer \(bearer)", forHTTPHeaderField: "Authorization") }
        request.httpBody = try encoder.encode(body)
        return try await send(request)
    }

    private func send<Result: Decodable>(_ request: URLRequest) async throws -> Result {
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse else { throw BackendError.invalidResponse }
        guard (200..<300).contains(http.statusCode) else {
            let payload = (try? JSONSerialization.jsonObject(with: data) as? [String: Any])
            throw BackendError.http(http.statusCode, payload?["error"] as? String ?? "Request failed.")
        }
        do {
            return try decoder.decode(Result.self, from: data)
        } catch let error as DecodingError {
            throw BackendError.decoding(Self.describe(error))
        }
    }

    private func url(_ path: String) -> URL {
        baseURL.appendingPathComponent(path.trimmingCharacters(in: CharacterSet(charactersIn: "/")))
    }

    private func correlate(_ request: inout URLRequest, turnId: String, operationId: String) {
        request.setValue(turnId, forHTTPHeaderField: "X-Phantom-Correlation-Id")
        request.setValue(operationId, forHTTPHeaderField: "X-Phantom-Operation-Id")
    }

    private static func describe(_ error: DecodingError) -> String {
        let context: DecodingError.Context
        let field: String
        switch error {
        case .keyNotFound(let key, let value): context = value; field = key.stringValue
        case .valueNotFound(_, let value): context = value; field = value.codingPath.last?.stringValue ?? "value"
        case .typeMismatch(_, let value): context = value; field = value.codingPath.last?.stringValue ?? "value"
        case .dataCorrupted(let value): context = value; field = value.codingPath.last?.stringValue ?? "response"
        @unknown default: return "The backend response format is unsupported."
        }
        let path = (context.codingPath.map(\.stringValue) + [field]).filter { !$0.isEmpty }.joined(separator: ".")
        return "The backend response is missing or has an invalid field: \(path)."
    }
}

private struct LogoutResult: Decodable {
    let revoked: Bool
}

private struct DeleteResult: Decodable {
    let deleted: Bool
}

private struct EmptyResult: Decodable {}

enum BackendError: LocalizedError {
    case invalidResponse
    case http(Int, String)
    case server(String)
    case decoding(String)

    var errorDescription: String? {
        switch self {
        case .invalidResponse:
            return "The Phantom backend returned an invalid response."
        case .http(_, let message), .server(let message), .decoding(let message):
            return message
        }
    }
}
