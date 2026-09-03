import Foundation

enum CreditLedger: String, Codable { case pro, premium }

enum InterviewUsageSource: String, Codable {
    case freeTrialManaged
    case proBYO
    case premiumManaged
    case premiumDebtExtension
}

enum InterviewSessionState: String, Codable { case active, paused, completed }
enum UsageSyncStatus: String, Codable { case pending, synced, failed, deadLetter }

struct RuntimeWallet: Codable, Equatable {
    var proAvailableCredits: Decimal
    var premiumAvailableCredits: Decimal
    var premiumNegativeCredits: Decimal
}

struct InterviewUsageSegment: Codable, Equatable {
    var source: InterviewUsageSource
    var providerId: String
    var startedMeteredSecond: Int
    var endedMeteredSecond: Int?
}

struct InterviewSession: Codable, Equatable {
    var sessionId: String
    var userId: String
    var startedAtUtc: Date
    var lastHeartbeatAtUtc: Date
    var lockExpiresAtUtc: Date?
    var endedAtUtc: Date?
    var pausedAtUtc: Date?
    var totalPausedSeconds: Int = 0
    var usageSegments: [InterviewUsageSegment] = []
    var state: InterviewSessionState = .active
    var primaryLedger: CreditLedger
    var lockToken: String
    var heartbeatIntervalSeconds: Int = 60
    var lockTtlSeconds: Int = 300
    var questionInputs: [String] = []
    var chargedCredits: Decimal = 0
    var chargedBlocks: Int = 0
    var premiumDebtAdded: Decimal = 0
}

struct InterviewActivation {
    let allowed: Bool
    let startedNewSession: Bool
    let resumedExistingSession: Bool
    let title: String
    let message: String
    let session: InterviewSession?
}

struct RuntimeMeteringStatus {
    let session: InterviewSession?
    let elapsed: TimeInterval
    let projectedCharge: Decimal
    let shouldFinalize: Bool
    let boundaryMessage: String
}

struct RuntimeDiagnostics {
    let pendingUsage: Int
    let failedUsage: Int
    let deadLetters: [UsageReconciliationRecord]
    let queuedTelemetry: Int
    let droppedTelemetry: Int
}

struct InterviewCompletion: Codable, Equatable {
    let userId: String
    let sessionId: String
    let startedAtUtc: Date
    let endedAtUtc: Date
    let chargedCredits: Decimal
    let chargedBlocks: Int
    let consumedProCredits: Decimal
    let consumedPremiumCredits: Decimal
    let premiumDebtAdded: Decimal
    let remainingProCredits: Decimal
    let remainingPremiumCredits: Decimal
    let questionInputs: [String]

    var reconciliation: UsageReconciliationPayload {
        UsageReconciliationPayload(
            userId: userId,
            sessionId: sessionId,
            startedAtUtc: startedAtUtc,
            endedAtUtc: endedAtUtc,
            chargedCredits: chargedCredits,
            chargedBlocks: chargedBlocks,
            consumedProCredits: consumedProCredits,
            consumedPremiumCredits: consumedPremiumCredits,
            premiumDebtAdded: premiumDebtAdded,
            questionInputs: questionInputs
        )
    }
}

struct UsageReconciliationPayload: Codable, Equatable {
    let userId: String
    let sessionId: String
    let startedAtUtc: Date
    let endedAtUtc: Date
    let chargedCredits: Decimal
    let chargedBlocks: Int
    let consumedProCredits: Decimal
    let consumedPremiumCredits: Decimal
    let premiumDebtAdded: Decimal
    let questionInputs: [String]
}

struct UsageReconciliationRecord: Codable, Equatable {
    var recordId: String
    var payload: UsageReconciliationPayload
    var status: UsageSyncStatus = .pending
    var attemptCount: Int = 0
    var lastError: String = ""
    var ledgerEntryId: String = ""
    var createdAtUtc: Date = Date()
    var lastAttemptAtUtc: Date?
    var syncedAtUtc: Date?
    var deadLetteredAtUtc: Date?
}

struct PhantomTelemetryEvent: Codable, Equatable {
    let eventId: String
    let category: String
    let eventName: String
    let occurredAtUtc: Date
    let attributes: [String: String]
}

struct RuntimeState: Codable {
    var activeSession: InterviewSession?
    var wallet: RuntimeWallet?
    var reconciliations: [UsageReconciliationRecord] = []
    var telemetry: [PhantomTelemetryEvent] = []
    var droppedTelemetry: Int?
}

struct DeviceLockResult: Decodable {
    let acquired: Bool
    let lockToken: String
    let expiresAtUtc: Date
    let holderDeviceId: String
    let holderSessionId: String
}

struct UsageReconciliationResult: Decodable {
    let accepted: Bool
    let ledgerEntryId: String
    let appliedCredits: Decimal
    let addedPremiumDebt: Decimal
}
