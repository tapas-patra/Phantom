import CryptoKit
import Foundation

actor CreditMeteringService {
    static let protectedContinuationCap: Decimal = 1
    private let persistence: RuntimePersistence

    init(persistence: RuntimePersistence) { self.persistence = persistence }

    func configure(wallet: RuntimeWallet) async throws {
        try await persistence.setWallet(wallet)
    }

    func ensureSession(
        userId: String,
        emailVerified: Bool,
        tier: String,
        preferBYO: Bool,
        canUseBYO: Bool,
        canStartNewInterview: Bool,
        resumableSessionId: String,
        allowPaidExtension: Bool,
        now: Date = Date()
    ) async throws -> InterviewActivation {
        var state = await persistence.snapshot()
        if var session = state.activeSession, session.state == .active || session.state == .paused {
            let expiry = session.lockExpiresAtUtc
                ?? session.lastHeartbeatAtUtc.addingTimeInterval(TimeInterval(session.lockTtlSeconds))
            if expiry > now,
               session.userId == userId,
               resumableSessionId.isEmpty || session.sessionId == resumableSessionId {
                return InterviewActivation(
                    allowed: true,
                    startedNewSession: false,
                    resumedExistingSession: true,
                    title: session.state == .paused ? "Interview Paused" : "Interview Resumed",
                    message: session.state == .paused
                        ? "The current interview stays paused until a response succeeds."
                        : "Resuming the current interview session on this device.",
                    session: session
                )
            }
            session.state = .completed
            session.endedAtUtc = session.endedAtUtc ?? now
            try await persistence.setSession(session)
            state.activeSession = session
        }

        guard canStartNewInterview else { return denied("Interview Start Restricted", "Only the already locked interview may resume until account validation succeeds.") }
        guard emailVerified else { return denied("Email Verification Required", "Email verification must complete before an interview can start.") }
        guard let wallet = state.wallet else { return denied("Account Check Required", "No cached account snapshot is available for interview metering.") }
        guard wallet.premiumNegativeCredits <= 0 else { return denied("Negative Premium Balance", "New interviews stay blocked until the Premium debt is cleared.") }

        let free = tier.lowercased() == "free"
        let ledger: CreditLedger?
        if !free, canUseBYO, preferBYO, wallet.proAvailableCredits > 0 { ledger = .pro }
        else if !free, wallet.premiumAvailableCredits > 0 { ledger = .premium }
        else if canUseBYO, wallet.proAvailableCredits > 0 { ledger = .pro }
        else if wallet.premiumAvailableCredits > 0 { ledger = .premium }
        else if !free, allowPaidExtension, wallet.premiumNegativeCredits < Self.protectedContinuationCap { ledger = .premium }
        else { ledger = nil }

        guard let ledger else {
            return free
                ? denied("Free Trial Exhausted", "Some free-trial credit must be available before a new interview starts.")
                : denied("No Credits Available", "Some paid credit must be available before a new interview starts.")
        }

        let rawToken = UUID().uuidString.replacingOccurrences(of: "-", with: "")
        let token = SHA256.hash(data: Data(rawToken.utf8)).map { String(format: "%02X", $0) }.joined()
        let session = InterviewSession(
            sessionId: "session-\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())",
            userId: userId,
            startedAtUtc: now,
            lastHeartbeatAtUtc: now,
            lockExpiresAtUtc: now.addingTimeInterval(300),
            primaryLedger: ledger,
            lockToken: token
        )
        try await persistence.setSession(session)
        return InterviewActivation(
            allowed: true,
            startedNewSession: true,
            resumedExistingSession: false,
            title: "Interview Started",
            message: "Metering started on the \(ledger.rawValue) balance.",
            session: session
        )
    }

    func replaceLock(token: String, expiresAt: Date, now: Date = Date()) async throws {
        guard var session = await persistence.snapshot().activeSession else { return }
        session.lockToken = token
        session.lastHeartbeatAtUtc = now
        session.lockExpiresAtUtc = expiresAt
        try await persistence.setSession(session)
    }

    func heartbeat(expiresAt: Date, now: Date = Date()) async throws {
        guard var session = await persistence.snapshot().activeSession else { return }
        session.lastHeartbeatAtUtc = now
        session.lockExpiresAtUtc = expiresAt
        try await persistence.setSession(session)
    }

    func activeSession() async -> InterviewSession? {
        let session = await persistence.snapshot().activeSession
        return session?.state == .active || session?.state == .paused ? session : nil
    }

    func status(
        tier: String,
        allowFreeTrialExtension: Bool,
        allowPaidExtension: Bool,
        now: Date = Date()
    ) async -> RuntimeMeteringStatus {
        guard let session = await activeSession() else {
            return RuntimeMeteringStatus(session: nil, elapsed: 0, projectedCharge: 0, shouldFinalize: false, boundaryMessage: "")
        }
        let elapsed = meteredElapsed(session, now: now)
        let projected = Self.estimateCharge(seconds: elapsed)
        guard session.state == .active, let wallet = await persistence.snapshot().wallet else {
            return RuntimeMeteringStatus(session: session, elapsed: elapsed, projectedCharge: projected, shouldFinalize: false, boundaryMessage: "")
        }
        if tier.lowercased() == "free" {
            let stop = Self.shouldFinalizeBoundary(tier: tier, elapsed: elapsed, projectedCharge: projected, paidAvailable: 0, existingDebt: 0, allowFreeTrialExtension: allowFreeTrialExtension, allowPaidExtension: allowPaidExtension)
            let message = allowFreeTrialExtension
                ? "The second 15-minute demo block ended."
                : "The first 15-minute demo block ended. Enable free-trial extension in Settings to continue."
            return RuntimeMeteringStatus(session: session, elapsed: elapsed, projectedCharge: projected, shouldFinalize: stop, boundaryMessage: stop ? message : "")
        }
        let paidAvailable = max(0, wallet.proAvailableCredits) + max(0, wallet.premiumAvailableCredits)
        let stop = Self.shouldFinalizeBoundary(tier: tier, elapsed: elapsed, projectedCharge: projected, paidAvailable: paidAvailable, existingDebt: wallet.premiumNegativeCredits, allowFreeTrialExtension: allowFreeTrialExtension, allowPaidExtension: allowPaidExtension)
        return RuntimeMeteringStatus(session: session, elapsed: elapsed, projectedCharge: projected, shouldFinalize: stop, boundaryMessage: stop ? (allowPaidExtension ? "The protected 1-credit continuation limit was reached." : "Available paid credits were consumed. Enable paid extension in Settings to continue.") : "")
    }

    static func shouldFinalizeBoundary(
        tier: String,
        elapsed: TimeInterval,
        projectedCharge: Decimal,
        paidAvailable: Decimal,
        existingDebt: Decimal,
        allowFreeTrialExtension: Bool,
        allowPaidExtension: Bool
    ) -> Bool {
        if tier.lowercased() == "free" { return elapsed >= (allowFreeTrialExtension ? 1_800 : 900) }
        return allowPaidExtension
            ? existingDebt + max(0, projectedCharge - paidAvailable) >= protectedContinuationCap
            : projectedCharge >= paidAvailable
    }

    func trackQuestion(_ input: String) async throws {
        guard var session = await activeSession(), session.questionInputs.count < 200 else { return }
        let value = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return }
        session.questionInputs.append(String(value.prefix(4_000)))
        try await persistence.setSession(session)
    }

    func trackSource(_ source: InterviewUsageSource, providerId: String, now: Date = Date()) async throws {
        guard var session = await activeSession() else { return }
        let second = Int(meteredElapsed(session, now: now).rounded(.down))
        if let last = session.usageSegments.last,
           last.source == source,
           last.providerId.caseInsensitiveCompare(providerId) == .orderedSame,
           last.endedMeteredSecond == nil { return }
        if !session.usageSegments.isEmpty, session.usageSegments[session.usageSegments.count - 1].endedMeteredSecond == nil {
            session.usageSegments[session.usageSegments.count - 1].endedMeteredSecond = second
        }
        session.usageSegments.append(InterviewUsageSegment(
            source: source,
            providerId: providerId,
            startedMeteredSecond: second
        ))
        try await persistence.setSession(session)
    }

    func pause(now: Date = Date()) async throws {
        guard var session = await activeSession(), session.state == .active else { return }
        session.state = .paused
        session.pausedAtUtc = now
        try await persistence.setSession(session)
    }

    func resume(now: Date = Date()) async throws {
        guard var session = await activeSession(), session.state == .paused, let paused = session.pausedAtUtc else { return }
        session.totalPausedSeconds += max(0, Int(now.timeIntervalSince(paused).rounded(.down)))
        session.pausedAtUtc = nil
        session.state = .active
        session.lastHeartbeatAtUtc = now
        try await persistence.setSession(session)
    }

    func finalize(tier: String, preferBYO: Bool, canUseBYO: Bool, now: Date = Date()) async throws -> InterviewCompletion? {
        guard var session = await activeSession(), var wallet = await persistence.snapshot().wallet else { return nil }
        let duration = meteredElapsed(session, now: now)
        let totalSeconds = max(0, Int(duration.rounded(.down)))
        let blocks = max(1, Int(ceil(duration / 900)))
        let requested = Self.estimateCharge(seconds: duration)
        if !session.usageSegments.isEmpty, session.usageSegments[session.usageSegments.count - 1].endedMeteredSecond == nil {
            session.usageSegments[session.usageSegments.count - 1].endedMeteredSecond = totalSeconds
        }
        let bySource = allocate(session: session, requested: requested, totalSeconds: totalSeconds)
        let requestedBYO = bySource[.proBYO] ?? 0
        let requestedPremium = bySource[.premiumManaged] ?? 0
        let extensionCharge = bySource[.premiumDebtExtension] ?? 0
        var usedPro = min(wallet.proAvailableCredits, requestedBYO)
        var usedPremium = min(wallet.premiumAvailableCredits, requestedPremium)
        var remainingBYO = max(0, requestedBYO - usedPro)
        var remainingPremium = max(0, requestedPremium - usedPremium)

        if preferBYO {
            if canUseBYO, remainingPremium > 0, wallet.proAvailableCredits > usedPro {
                let spill = min(wallet.proAvailableCredits - usedPro, remainingPremium)
                usedPro += spill; remainingPremium -= spill
            }
            if remainingBYO > 0, wallet.premiumAvailableCredits > usedPremium {
                let spill = min(wallet.premiumAvailableCredits - usedPremium, remainingBYO)
                usedPremium += spill; remainingBYO -= spill
            }
        } else {
            if remainingBYO > 0, wallet.premiumAvailableCredits > usedPremium {
                let spill = min(wallet.premiumAvailableCredits - usedPremium, remainingBYO)
                usedPremium += spill; remainingBYO -= spill
            }
            if canUseBYO, remainingPremium > 0, wallet.proAvailableCredits > usedPro {
                let spill = min(wallet.proAvailableCredits - usedPro, remainingPremium)
                usedPro += spill; remainingPremium -= spill
            }
        }

        let free = tier.lowercased() == "free"
        var debt: Decimal = 0
        if !free {
            var budget = max(0, Self.protectedContinuationCap - wallet.premiumNegativeCredits)
            let shortfall = remainingBYO + remainingPremium
            let shortfallDebt = min(shortfall, budget)
            debt += shortfallDebt; budget -= shortfallDebt
            debt += min(extensionCharge, budget)
        }

        let charged: Decimal
        if free {
            let available = session.primaryLedger == .pro ? wallet.proAvailableCredits : wallet.premiumAvailableCredits
            charged = min(available, requested)
            if session.primaryLedger == .pro { wallet.proAvailableCredits = max(0, wallet.proAvailableCredits - charged) }
            else { wallet.premiumAvailableCredits = max(0, wallet.premiumAvailableCredits - charged) }
        } else {
            charged = usedPro + usedPremium + debt
            wallet.proAvailableCredits = max(0, wallet.proAvailableCredits - usedPro)
            wallet.premiumAvailableCredits = max(0, wallet.premiumAvailableCredits - usedPremium)
        }
        wallet.premiumNegativeCredits += debt

        session.state = .completed
        session.endedAtUtc = now
        session.totalPausedSeconds = max(0, Int((now.timeIntervalSince(session.startedAtUtc) - duration).rounded(.down)))
        session.pausedAtUtc = nil
        session.chargedBlocks = blocks
        session.chargedCredits = charged
        session.premiumDebtAdded = debt
        session.lastHeartbeatAtUtc = now
        try await persistence.setSession(session)
        try await persistence.setWallet(wallet)

        return InterviewCompletion(
            userId: session.userId,
            sessionId: session.sessionId,
            startedAtUtc: session.startedAtUtc,
            endedAtUtc: now,
            chargedCredits: charged,
            chargedBlocks: blocks,
            consumedProCredits: usedPro,
            consumedPremiumCredits: usedPremium,
            premiumDebtAdded: debt,
            remainingProCredits: wallet.proAvailableCredits,
            remainingPremiumCredits: wallet.premiumAvailableCredits,
            questionInputs: session.questionInputs
        )
    }

    func abandon(now: Date = Date()) async throws {
        guard var session = await activeSession() else { return }
        if session.state == .paused, let paused = session.pausedAtUtc {
            session.totalPausedSeconds += max(0, Int(now.timeIntervalSince(paused).rounded(.down)))
        }
        session.state = .completed
        session.endedAtUtc = now
        session.pausedAtUtc = nil
        session.chargedBlocks = 0
        session.chargedCredits = 0
        session.premiumDebtAdded = 0
        try await persistence.setSession(session)
    }

    static func estimateCharge(seconds: TimeInterval) -> Decimal {
        rounded(Decimal(max(0, Int(seconds.rounded(.down)) / 60)) / Decimal(60), scale: 2, mode: .plain)
    }

    private func meteredElapsed(_ session: InterviewSession, now: Date) -> TimeInterval {
        let end = session.state == .completed ? session.endedAtUtc ?? now : now
        var paused = session.totalPausedSeconds
        if session.state == .paused, let pausedAt = session.pausedAtUtc {
            paused += max(0, Int(end.timeIntervalSince(pausedAt).rounded(.down)))
        }
        return max(0, end.timeIntervalSince(session.startedAtUtc) - Double(paused))
    }

    private func allocate(session: InterviewSession, requested: Decimal, totalSeconds: Int) -> [InterviewUsageSource: Decimal] {
        var seconds: [InterviewUsageSource: Int] = [:]
        var ordered: [InterviewUsageSource] = []
        for segment in session.usageSegments {
            let count = max(0, (segment.endedMeteredSecond ?? totalSeconds) - segment.startedMeteredSecond)
            guard count > 0 else { continue }
            if seconds[segment.source] == nil { ordered.append(segment.source) }
            seconds[segment.source, default: 0] += count
        }
        if ordered.isEmpty {
            let source: InterviewUsageSource = session.primaryLedger == .pro ? .proBYO : .premiumManaged
            ordered = [source]; seconds[source] = max(1, totalSeconds)
        }
        let denominator = max(1, seconds.values.reduce(0, +))
        var remaining = requested
        var result: [InterviewUsageSource: Decimal] = [:]
        for (index, source) in ordered.enumerated() {
            if index == ordered.count - 1 { result[source] = Self.rounded(max(0, remaining), scale: 2, mode: .plain) }
            else {
                let share = Self.rounded(requested * Decimal(seconds[source] ?? 0) / Decimal(denominator), scale: 2, mode: .down)
                result[source] = min(share, remaining)
                remaining -= min(share, remaining)
            }
        }
        return result
    }

    private static func rounded(_ value: Decimal, scale: Int, mode: Decimal.RoundingMode) -> Decimal {
        var input = value
        var output = Decimal()
        NSDecimalRound(&output, &input, scale, mode)
        return output
    }

    private func denied(_ title: String, _ message: String) -> InterviewActivation {
        InterviewActivation(allowed: false, startedNewSession: false, resumedExistingSession: false, title: title, message: message, session: nil)
    }
}
