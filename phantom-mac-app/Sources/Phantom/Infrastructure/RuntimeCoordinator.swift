import Foundation

actor RuntimeCoordinator {
    let metering: CreditMeteringService
    nonisolated let storageFailure: String?
    private let persistence: RuntimePersistence
    private let backend: BackendClient
    private let device: DeviceIdentity
    private var telemetryFlushTask: Task<Void, Never>?

    init(backend: BackendClient, device: DeviceIdentity) {
        let persistence = RuntimePersistence()
        self.persistence = persistence
        self.metering = CreditMeteringService(persistence: persistence)
        self.backend = backend
        self.device = device
        storageFailure = persistence.bootstrapFailure
    }

    func configure(snapshot: StartupSnapshot) async throws {
        try await metering.configure(wallet: snapshot.runtimeWallet)
    }

    func beginInterview(
        auth: AuthSession,
        account: StartupSnapshot,
        preferBYO: Bool,
        canUseBYO: Bool,
        canStartNewInterview: Bool,
        allowPaidExtension: Bool
    ) async throws -> InterviewActivation {
        let activation = try await metering.ensureSession(
            userId: auth.userId,
            emailVerified: account.emailVerified,
            tier: account.accessTier,
            preferBYO: preferBYO,
            canUseBYO: canUseBYO,
            canStartNewInterview: canStartNewInterview,
            resumableSessionId: account.hasResumableLockedSession ? account.lastLockedSessionId : "",
            allowPaidExtension: allowPaidExtension
        )
        await track(
            category: "billing",
            event: activation.startedNewSession ? "interview_session_activated" : (activation.resumedExistingSession ? "interview_session_resumed" : "interview_session_denied"),
            attributes: ["title": activation.title, "tier": account.accessTier],
            accessToken: auth.accessToken
        )
        guard activation.allowed, let local = activation.session else { return activation }

        let needsHostedLock = activation.startedNewSession
            || local.lockToken.isEmpty
            || (local.lockExpiresAtUtc ?? .distantPast) <= Date()
        guard needsHostedLock else { return activation }

        do {
            let hosted = try await backend.acquireLock(
                accessToken: auth.accessToken,
                userId: auth.userId,
                deviceId: device.installId,
                sessionId: local.sessionId
            )
            guard hosted.acquired else {
                try await metering.abandon()
                return InterviewActivation(
                    allowed: false,
                    startedNewSession: false,
                    resumedExistingSession: false,
                    title: "Interview Locked Elsewhere",
                    message: hosted.holderDeviceId.isEmpty
                        ? "Another device already holds the active interview lock."
                        : "Another device already holds the active interview lock (\(hosted.holderDeviceId)).",
                    session: nil
                )
            }
            try await metering.replaceLock(token: hosted.lockToken, expiresAt: hosted.expiresAtUtc)
        } catch where !Self.isAuthorityRejection(error) {
            // Matches Windows: hosted lock transport failures retain the five-minute local lock.
            await track(category: "interview", event: "hosted_lock_fallback", attributes: ["error_code": Self.errorCode(error)])
        } catch {
            try? await metering.pause()
            await track(category: "interview", event: "hosted_lock_rejected", attributes: ["error_code": Self.errorCode(error)])
            throw error
        }
        return activation
    }

    func heartbeat(auth: AuthSession) async {
        guard let session = await metering.activeSession(), !session.lockToken.isEmpty else { return }
        do {
            let result = try await backend.heartbeatLock(
                accessToken: auth.accessToken,
                sessionId: session.sessionId,
                lockToken: session.lockToken,
                deviceId: device.installId
            )
            try await metering.heartbeat(expiresAt: result.expiresAtUtc)
            await track(category: "interview", event: "lock_heartbeat", attributes: ["sessionId": session.sessionId])
        } catch {
            if Self.isAuthorityRejection(error),
               let recovered = try? await backend.acquireLock(
                   accessToken: auth.accessToken,
                   userId: auth.userId,
                   deviceId: device.installId,
                   sessionId: session.sessionId
               ), recovered.acquired {
                try? await metering.replaceLock(token: recovered.lockToken, expiresAt: recovered.expiresAtUtc)
                await track(category: "interview", event: "lock_heartbeat_recovered", attributes: ["error_code": "authority_token_refreshed"])
                return
            }
            if Self.isAuthorityRejection(error) { try? await metering.pause() }
            await track(category: "interview", event: "lock_heartbeat_failed", attributes: ["error_code": Self.errorCode(error)])
        }
    }

    func meteringStatus(tier: String, allowFreeTrialExtension: Bool, allowPaidExtension: Bool) async -> RuntimeMeteringStatus {
        await metering.status(
            tier: tier,
            allowFreeTrialExtension: allowFreeTrialExtension,
            allowPaidExtension: allowPaidExtension
        )
    }

    func pauseForInactivity(accessToken: String?) async {
        try? await metering.pause()
        await track(category: "billing", event: "interview_session_auto_paused", attributes: ["reason": "inactivity"], accessToken: accessToken)
    }

    func finalize(auth: AuthSession?, account: StartupSnapshot, preferBYO: Bool, canUseBYO: Bool) async -> InterviewCompletion? {
        let locked = await metering.activeSession()
        let completion = try? await metering.finalize(
            tier: account.accessTier,
            preferBYO: preferBYO,
            canUseBYO: canUseBYO
        )
        if let completion { try? await persistence.enqueue(completion.reconciliation) }
        if let completion {
            await track(
                category: "billing",
                event: "interview_session_finalized",
                attributes: ["sessionId": completion.sessionId, "chargedCredits": "\(completion.chargedCredits)", "premiumDebt": "\(completion.premiumDebtAdded)"],
                accessToken: auth?.accessToken
            )
        }
        if let auth, let locked, !locked.lockToken.isEmpty {
            try? await backend.releaseLock(
                accessToken: auth.accessToken,
                sessionId: locked.sessionId,
                lockToken: locked.lockToken
            )
        }
        if let auth {
            await flushUsage(accessToken: auth.accessToken)
            scheduleTelemetryFlush(accessToken: auth.accessToken)
            await telemetryFlushTask?.value
        }
        return completion
    }

    func invalidateAuthentication(auth: AuthSession?) async {
        let locked = await metering.activeSession()
        try? await metering.abandon()
        if let auth, let locked, !locked.lockToken.isEmpty {
            try? await backend.releaseLock(
                accessToken: auth.accessToken,
                sessionId: locked.sessionId,
                lockToken: locked.lockToken
            )
        }
        await track(category: "auth", event: "desktop_session_invalidated")
    }

    func track(category: String, event: String, attributes: [String: String] = [:], accessToken: String? = nil) async {
        let value = PhantomTelemetryEvent(
            eventId: "telemetry-\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())",
            category: category,
            eventName: event,
            occurredAtUtc: Date(),
            attributes: attributes
        )
        try? await persistence.appendTelemetry(value)
        if let accessToken { scheduleTelemetryFlush(accessToken: accessToken) }
    }

    func flush(accessToken: String) async {
        await flushUsage(accessToken: accessToken)
        scheduleTelemetryFlush(accessToken: accessToken)
        await telemetryFlushTask?.value
    }

    func diagnostics() async -> RuntimeDiagnostics { await persistence.diagnostics() }

    private func flushUsage(accessToken: String) async {
        for var record in await persistence.pendingReconciliations() {
            if record.attemptCount >= 3 {
                record.status = .deadLetter
                record.deadLetteredAtUtc = Date()
                if record.lastError.isEmpty { record.lastError = "Usage reconciliation exceeded the retry limit." }
                try? await persistence.updateReconciliation(record)
                continue
            }
            record.attemptCount += 1
            record.lastAttemptAtUtc = Date()
            do {
                let response = try await backend.reconcileUsage(accessToken: accessToken, payload: record.payload)
                if response.accepted {
                    record.status = .synced
                    record.lastError = ""
                    record.ledgerEntryId = response.ledgerEntryId
                    record.syncedAtUtc = Date()
                } else {
                    record.status = record.attemptCount >= 3 ? .deadLetter : .failed
                    record.lastError = "Hosted usage reconciliation rejected the session."
                }
            } catch {
                record.status = record.attemptCount >= 3 ? .deadLetter : .failed
                record.lastError = Self.errorCode(error)
                if record.status == .deadLetter { record.deadLetteredAtUtc = Date() }
            }
            try? await persistence.updateReconciliation(record)
        }
    }

    private func scheduleTelemetryFlush(accessToken: String) {
        guard telemetryFlushTask == nil else { return }
        telemetryFlushTask = Task { await runTelemetryFlush(accessToken: accessToken) }
    }

    private func runTelemetryFlush(accessToken: String) async {
        defer { telemetryFlushTask = nil }
        while true {
            let pending = await persistence.pendingTelemetry().sorted(by: { $0.occurredAtUtc < $1.occurredAtUtc })
            guard !pending.isEmpty else { return }
            var sent = Set<String>()
            for event in pending {
                do {
                    try await backend.ingestTelemetry(accessToken: accessToken, event: event)
                    sent.insert(event.eventId)
                } catch {
                    if !sent.isEmpty { try? await persistence.removeTelemetry(ids: sent) }
                    return
                }
            }
            try? await persistence.removeTelemetry(ids: sent)
        }
    }

    private static func isAuthorityRejection(_ error: Error) -> Bool {
        guard case BackendError.http(let status, _) = error else { return false }
        return (400..<500).contains(status)
    }

    private static func errorCode(_ error: Error) -> String {
        if let error = error as? BackendError {
            switch error {
            case .http(let status, _): return "backend_\(status)"
            case .invalidResponse: return "invalid_response"
            case .decoding: return "decoding_failed"
            case .server: return "backend_failed"
            }
        }
        if let error = error as? URLError { return "url_\(error.code.rawValue)" }
        return "request_failed"
    }
}
