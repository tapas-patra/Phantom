import AppKit
import Foundation

@MainActor
final class PhantomStore: ObservableObject {
    enum Screen {
        case login
        case chat
        case settings
    }

    @Published var screen: Screen = .login
    @Published var email = ""
    @Published var password = ""
    @Published var status = "Checking saved session…" {
        didSet {
            let cleaned = UserFacingText.sanitize(status)
            if cleaned != status { status = cleaned }
        }
    }
    @Published var isBusy = false
    @Published var session: AuthSession?
    @Published var account: StartupSnapshot?
    @Published var launchContext = AppLaunchContext.checking
    @Published var hostedKnowledgeBase: StartupSnapshot.KnowledgeBase?
    @Published var providers: [ManagedProvider] = []
    @Published var contextPacks: [ContextPack] = []
    @Published var selectedContextPackId = ""
    @Published var contextPackName = ""
    @Published var contextPackStatus = "Premium accounts can sync reusable context packs."
    @Published var selectedProviderId = "" {
        didSet {
            UserDefaults.standard.set(selectedProviderId, forKey: "chat.provider")
            if let saved = UserDefaults.standard.string(forKey: "chat.model.\(selectedProviderId.lowercased())") {
                selectedModelId = saved
            }
            if let provider = selectedProvider,
               !provider.models.contains(where: { $0.modelId == selectedModelId }) {
                selectedModelId = provider.models.first?.modelId ?? ""
            }
            loadBYOKey()
            if oldValue != selectedProviderId { announceCompanionRuntime() }
        }
    }
    @Published var selectedModelId = "" {
        didSet {
            UserDefaults.standard.set(selectedModelId, forKey: "chat.model")
            if !selectedProviderId.isEmpty {
                UserDefaults.standard.set(selectedModelId, forKey: "chat.model.\(selectedProviderId.lowercased())")
            }
            if !attachedScreenshots.isEmpty, !selectedModelSupportsVision { removeScreenshot() }
            if oldValue != selectedModelId { announceCompanionRuntime() }
        }
    }
    @Published var prompt = "" {
        didSet {
            guard !applyingCompanionComposer else { return }
            scheduleCompanionComposerMirror()
        }
    }
    @Published var messages: [ChatMessage] = []
    @Published var isSending = false
    @Published var copilotMode: CopilotMode {
        didSet {
            UserDefaults.standard.set(copilotMode.rawValue, forKey: "copilot.mode")
            guard oldValue != copilotMode else { return }
            messages.removeAll()
            ConversationStore.clear()
            conversationManager.reset(mode: copilotMode)
        }
    }
    @Published var interviewDeliveryStyle: InterviewDeliveryStyle {
        didSet { UserDefaults.standard.set(interviewDeliveryStyle.rawValue, forKey: "copilot.deliveryStyle") }
    }
    @Published var resumeText: String {
        didSet {
            UserDefaults.standard.set(resumeText, forKey: "context.resume")
            scheduleContextWarmup()
        }
    }
    @Published var jobDescriptionText: String {
        didSet {
            UserDefaults.standard.set(jobDescriptionText, forKey: "context.jobDescription")
            scheduleContextWarmup()
        }
    }
    @Published var resumeSummary = ""
    @Published var jobDescriptionSummary = ""
    @Published var contextSummaryStatus = "Context summaries will warm before the first request."
    @Published var diagnosticsText = ""
    @Published var legacyAppPath: String {
        didSet { UserDefaults.standard.set(legacyAppPath, forKey: "power.legacyAppPath") }
    }
    @Published var debugModeEnabled: Bool {
        didSet { UserDefaults.standard.set(debugModeEnabled, forKey: "debug.enabled") }
    }
    @Published var debugErrorSimulation: String {
        didSet { UserDefaults.standard.set(debugErrorSimulation, forKey: "debug.simulation") }
    }
    @Published var debugRequestCount = 0
    @Published var attachedScreenshots: [Data] = []
    @Published var isScreenshotPreviewVisible = false
    @Published var isCapturingScreenshot = false
    static let maxAttachedScreenshots = 3

    // Companion Mode hooks. Set by CompanionCommandHost so streamed deltas and turn
    // completions are forwarded to the phone relay without the relay layer knowing about
    // the chat pipeline internals.
    @Published var companionSelectedDisplayId: String = ""
    var companionRequestId: String?
    var companionDeltaHandler: ((String) -> Void)?
    var companionTurnFinishedHandler: ((Bool) -> Void)?
    var companionSessionStartedHandler: (() -> Void)?
    var companionVoiceTranscriptHandler: ((String, Bool, Bool) -> Void)?
    // Companion capture/error status flags (spec §5.2: status may be `capturing` or
    // `error`). Set/cleared by CompanionCommandHost around headless capture and on
    // turn outcome; read by desktopStatus() for desktop.hello / session.snapshot (H1).
    @Published var companionIsCapturing: Bool = false
    @Published var companionHasError: Bool = false
    @Published var companionInterviewActive: Bool = false
    // True while companion mode has hidden the overlay (spec §8.2). Driven by the
    // orchestrator via setCompanionOverlayHidden(); PhantomMain observes this to
    // order out / restore the window without activating the app.
    @Published var companionOverlayHidden: Bool = false
    var onCompanionOverlayHidden: ((Bool) -> Void)?
    static let maxResumeWords = 1_200
    static let maxJobDescriptionWords = 450
    @Published var isCompact = false
    @Published var isListening = false
    @Published var voiceStatus = "Ready" {
        didSet {
            let cleaned = UserFacingText.sanitize(voiceStatus)
            if cleaned != voiceStatus { voiceStatus = cleaned }
        }
    }
    @Published var voiceEnabled: Bool {
        didSet { UserDefaults.standard.set(voiceEnabled, forKey: "voice.enabled") }
    }
    @Published var autoSendAfterVoiceStop: Bool {
        didSet { UserDefaults.standard.set(autoSendAfterVoiceStop, forKey: "voice.autoSend") }
    }
    @Published var speechRecognitionMode: String { didSet { UserDefaults.standard.set(speechRecognitionMode, forKey: "speech.mode") } }
    @Published var speechProviders: [ManagedProvider] = []
    @Published var selectedSpeechProviderId: String { didSet { UserDefaults.standard.set(selectedSpeechProviderId, forKey: "speech.provider"); selectSpeechModel(); loadSpeechKeys() } }
    @Published var selectedSpeechModelId: String { didSet { UserDefaults.standard.set(selectedSpeechModelId, forKey: "speech.model") } }
    @Published var speechLanguage: String { didSet { UserDefaults.standard.set(speechLanguage, forKey: "speech.language") } }
    @Published var useChatKeysForSpeech: Bool { didSet { UserDefaults.standard.set(useChatKeysForSpeech, forKey: "speech.useChatKeys") } }
    @Published var autoFallbackToNativeSpeech: Bool { didSet { UserDefaults.standard.set(autoFallbackToNativeSpeech, forKey: "speech.nativeFallback") } }
    @Published var speechAPIKey = ""
    @Published var speechSecondAPIKey = ""
    @Published var speechKeyStatus = "Dedicated speech keys are stored in macOS Keychain."
    @Published var opacity: Double {
        didSet {
            UserDefaults.standard.set(opacity, forKey: "window.opacity")
            onWindowPreferencesChanged?(opacity, clickThrough, useFakeCursor, fakeCursorScale)
        }
    }
    @Published var clickThrough: Bool {
        didSet {
            UserDefaults.standard.set(clickThrough, forKey: "window.clickThrough")
            onWindowPreferencesChanged?(opacity, clickThrough, useFakeCursor, fakeCursorScale)
        }
    }
    @Published var useFakeCursor: Bool {
        didSet {
            UserDefaults.standard.set(useFakeCursor, forKey: "window.useFakeCursor")
            onWindowPreferencesChanged?(opacity, clickThrough, useFakeCursor, fakeCursorScale)
        }
    }
    @Published var fakeCursorScale: Double {
        didSet {
            UserDefaults.standard.set(fakeCursorScale, forKey: "window.fakeCursorScale")
            onWindowPreferencesChanged?(opacity, clickThrough, useFakeCursor, fakeCursorScale)
        }
    }
    @Published var settingsClickThrough = false
    @Published var allowPaidSessionExtension: Bool {
        didSet { UserDefaults.standard.set(allowPaidSessionExtension, forKey: "chat.allowPaidExtension") }
    }
    @Published var allowFreeTrialSessionExtension: Bool {
        didSet { UserDefaults.standard.set(allowFreeTrialSessionExtension, forKey: "chat.allowFreeTrialExtension") }
    }
    @Published var autoPauseOnInactivity: Bool {
        didSet { UserDefaults.standard.set(autoPauseOnInactivity, forKey: "chat.autoPauseOnInactivity") }
    }
    @Published var inactivityMinutes: Int {
        didSet {
            if inactivityMinutes < 10 { inactivityMinutes = 10 }
            UserDefaults.standard.set(inactivityMinutes, forKey: "chat.inactivityMinutes")
        }
    }
    @Published var sessionTimerText = ""
    @Published var sessionStatusText = "Idle"
    @Published var sessionExtensionOptInRequired = false
    @Published var useBYOProvider: Bool {
        didSet { UserDefaults.standard.set(useBYOProvider, forKey: "chat.useBYO") }
    }
    @Published var preferBYOCreditsFirst: Bool {
        didSet {
            UserDefaults.standard.set(preferBYOCreditsFirst, forKey: "billing.preferBYO")
            syncRuntimeLane()
        }
    }
    @Published var byoAPIKey = ""
    @Published var byoSecondAPIKey = ""
    @Published var byoKeyStatus = "API keys are stored in macOS Keychain."
    @Published var autoSwitchKeysOnError: Bool {
        didSet { UserDefaults.standard.set(autoSwitchKeysOnError, forKey: "rotation.autoSwitchKeys") }
    }
    @Published var autoSwitchModelsOnError: Bool {
        didSet { UserDefaults.standard.set(autoSwitchModelsOnError, forKey: "rotation.autoSwitchModels") }
    }

    var onWindowPreferencesChanged: ((Double, Bool, Bool, Double) -> Void)?
    var onCaptureScreenshot: (() async throws -> Data)?
    var onCompactModeChanged: ((Bool) -> Void)?
    var onLogout: (() -> Void)?
    var onRestart: (() -> Bool)?
    var onLegacyHandoff: (() -> Void)?

    let configuration: HostedConfiguration
    private let backend: BackendClient
    private let byoClient = BYOClient()
    private let device: DeviceIdentity
    private let runtime: RuntimeCoordinator
    private let rotation = APIRotationManager()
    private let conversationManager = ConversationManager()
    private let speechInput = SpeechInputService()
    private lazy var speechClient = SpeechTranscriptionClient(backend: backend, rotation: rotation)
    private var managedProviders: [ManagedProvider] = []
    private var byoProviders: [ManagedProvider] = {
        // Wait for network fetch — never seed hardcoded BYOCatalog models.
        if let cached = BYOCatalogStore.load(), !cached.providers.isEmpty {
            return cached.providers
        }
        return []
    }()
    private var chatTask: Task<Void, Never>?
    private var heartbeatTask: Task<Void, Never>?
    private var sessionCleanupTask: Task<Void, Never>?
    private var sessionStatusTask: Task<Void, Never>?
    private var lastInterviewActivityAt = Date()
    private var boundaryFinalizationInProgress = false
    private var contextWarmupTask: Task<Void, Never>?
    private var preserveConversationOnTermination = false
    private var runtimeLaneOverride: Bool?
    private var meteringSessionId = ""
    private var laneChargeBaseline: Decimal = 0
    private var activeRequestId = ""
    private let copilotSessionId = UUID().uuidString
    private var activeTurnId = ""
    private var activeOperationId = ""
    private var activeExecutionLane = "managed"
    private var activeUsageSource = "premium_managed"
    private var lastProviderOperationHadOutput = false
    private var activeRequestStartedAt = Date()
    private var firstChunkRecorded = false
    private var settingsSnapshot: SettingsSnapshot?

    private struct SettingsSnapshot {
        let provider: String, model: String, mode: CopilotMode, style: InterviewDeliveryStyle, resume: String, job: String
        let opacity: Double, fakeCursor: Bool, fakeCursorScale: Double, freeExtension: Bool, paidExtension: Bool
        let autoPause: Bool, inactivity: Int, preferBYO: Bool, voice: Bool, autoVoice: Bool
        let speechMode: String, speechProvider: String, speechModel: String, speechLanguage: String, sharedSpeechKeys: Bool, speechFallback: Bool
        let legacyPath: String, debug: Bool, simulation: String
    }
    private var voicePromptPrefix = ""
    private var previousVoiceTranscript = ""
    private var lastVoiceRenderedPrompt = ""
    private var preserveVoiceEdits = false
    private var composerMirrorTask: Task<Void, Never>?
    private var applyingCompanionComposer = false
    private var lastMirroredComposer = ""
    private var pendingVoiceAutoSend = false
    private var pendingVoiceTurnId: String?
    private var lastAutoSentVoiceText = ""
    private var voiceCaptureSafetyTask: Task<Void, Never>?

    init() {
        let defaults = UserDefaults.standard
        configuration = HostedConfiguration.load()
        let backendURL = URL(string: configuration.desktopBackendBaseUrl)
            ?? URL(string: "https://phantom-ai-windows-app-backend.onrender.com")!
        let backend = BackendClient(baseURL: backendURL)
        let device = DeviceIdentity.load()
        self.backend = backend
        self.device = device
        runtime = RuntimeCoordinator(backend: backend, device: device)
        opacity = defaults.object(forKey: "window.opacity") == nil
            ? 0.92
            : defaults.double(forKey: "window.opacity")
        clickThrough = defaults.bool(forKey: "window.clickThrough")
        useFakeCursor = defaults.bool(forKey: "window.useFakeCursor")
        fakeCursorScale = FakeCursorCoordinator.clampedScale(
            defaults.object(forKey: "window.fakeCursorScale") == nil
                ? 1.0
                : defaults.double(forKey: "window.fakeCursorScale")
        )
        allowPaidSessionExtension = defaults.bool(forKey: "chat.allowPaidExtension")
        allowFreeTrialSessionExtension = defaults.bool(forKey: "chat.allowFreeTrialExtension")
        autoPauseOnInactivity = defaults.object(forKey: "chat.autoPauseOnInactivity") == nil
            ? true
            : defaults.bool(forKey: "chat.autoPauseOnInactivity")
        inactivityMinutes = max(10, defaults.object(forKey: "chat.inactivityMinutes") == nil ? 10 : defaults.integer(forKey: "chat.inactivityMinutes"))
        useBYOProvider = defaults.bool(forKey: "chat.useBYO")
        preferBYOCreditsFirst = defaults.bool(forKey: "billing.preferBYO")
        autoSwitchKeysOnError = defaults.object(forKey: "rotation.autoSwitchKeys") == nil
            ? true
            : defaults.bool(forKey: "rotation.autoSwitchKeys")
        autoSwitchModelsOnError = defaults.object(forKey: "rotation.autoSwitchModels") == nil
            ? true
            : defaults.bool(forKey: "rotation.autoSwitchModels")
        voiceEnabled = defaults.object(forKey: "voice.enabled") == nil
            ? true
            : defaults.bool(forKey: "voice.enabled")
        autoSendAfterVoiceStop = defaults.object(forKey: "voice.autoSend") == nil
            ? true
            : defaults.bool(forKey: "voice.autoSend")
        speechRecognitionMode = defaults.string(forKey: "speech.mode") ?? "Native"
        selectedSpeechProviderId = defaults.string(forKey: "speech.provider") ?? "ChatGPT"
        selectedSpeechModelId = defaults.string(forKey: "speech.model") ?? ""
        speechLanguage = defaults.string(forKey: "speech.language") ?? "en"
        useChatKeysForSpeech = defaults.object(forKey: "speech.useChatKeys") == nil ? true : defaults.bool(forKey: "speech.useChatKeys")
        autoFallbackToNativeSpeech = defaults.object(forKey: "speech.nativeFallback") == nil ? true : defaults.bool(forKey: "speech.nativeFallback")
        copilotMode = CopilotMode(rawValue: defaults.string(forKey: "copilot.mode") ?? "") ?? .interview
        interviewDeliveryStyle = InterviewDeliveryStyle(rawValue: defaults.string(forKey: "copilot.deliveryStyle") ?? "") ?? .standard
        resumeText = defaults.string(forKey: "context.resume") ?? ""
        jobDescriptionText = defaults.string(forKey: "context.jobDescription") ?? ""
        legacyAppPath = defaults.string(forKey: "power.legacyAppPath") ?? ""
        debugModeEnabled = defaults.bool(forKey: "debug.enabled")
        debugErrorSimulation = defaults.string(forKey: "debug.simulation") ?? "None"
        selectedProviderId = defaults.string(forKey: "chat.provider") ?? ""
        selectedModelId = defaults.string(forKey: "chat.model") ?? ""
        messages = ConversationStore.load()
        loadBYOKey()
        speechProviders = SpeechCatalogStore.load()?.providers ?? []
        selectSpeechModel()
        loadSpeechKeys()
        configureSpeechInput()
        scheduleContextWarmup()
        companion = CompanionOrchestrator(backend: backend, store: self)
        companionEnabled = defaults.bool(forKey: "companion.enabled")
        companionPairingId = defaults.string(forKey: "companion.pairingId") ?? ""
    }

    private(set) var companion: CompanionOrchestrator? = nil
    @Published var companionEnabled: Bool = false {
        didSet {
            UserDefaults.standard.set(companionEnabled, forKey: "companion.enabled")
            if companionEnabled {
                // Reset transient capture/error flags when companion mode is (re)enabled (H1).
                companionIsCapturing = false
                companionHasError = false
            }
        }
    }
    @Published var companionPairingId: String = "" {
        didSet { UserDefaults.standard.set(companionPairingId, forKey: "companion.pairingId") }
    }
    @Published var companionPairingCode: String = ""
    @Published var companionPairingQrPayload: String = ""
    @Published var companionStatusText: String = "Not paired"
    @Published var companionRelayState: CompanionRelayState = .disconnected
    private var companionDiscoveryTask: Task<Void, Never>?

    func startCompanionPairing() {
        guard let session, !session.accessToken.isEmpty else {
            companionStatusText = "Sign in to pair a phone."
            return
        }
        let label = device.label
        let version = AppVersion.current
        Task { [weak self] in
            guard let self else { return }
            do {
                let result = try await backend.startPairing(accessToken: session.accessToken, deviceLabel: label, appVersion: version)
                self.companionPairingCode = result.code
                self.companionPairingQrPayload = result.qrPayload
                self.companionStatusText = "Pairing code ready. Open the phone app and scan the QR or enter the code."
                self.startCompanionDiscoveryPolling(accessToken: session.accessToken, codeExpiresAt: result.expiresAtUtc)
            } catch {
                self.companionStatusText = "Update / backend not ready."
                print("[companion] pairing failed: \(error.localizedDescription)")
            }
        }
    }

    /// Polls `listPairings` until the phone completes the pairing (a pairing whose
    /// desktop platform matches this device appears), then stores the pairing id,
    /// enables Companion Mode, and reconciles the relay. Stops when the code expires.
    private func startCompanionDiscoveryPolling(accessToken: String, codeExpiresAt: Date) {
        companionDiscoveryTask?.cancel()
        companionDiscoveryTask = Task { [weak self] in
            guard let self else { return }
            while !Task.isCancelled {
                if Date() >= codeExpiresAt {
                    await MainActor.run {
                        if self.companionPairingId.isEmpty {
                            self.companionStatusText = "Pairing code expired. Generate a new code."
                        }
                    }
                    return
                }
                try? await Task.sleep(nanoseconds: 3_000_000_000)
                guard !Task.isCancelled else { return }
                do {
                    let list = try await self.backend.listPairings(accessToken: accessToken)
                    let active = list.pairings.first(where: { $0.desktopPlatform == "macos" })
                    if let pairing = active {
                        await MainActor.run {
                            self.companionPairingId = pairing.pairingId
                            self.companionPairingCode = ""
                            self.companionPairingQrPayload = ""
                            self.companionEnabled = true
                            self.companionStatusText = "Paired. Connecting to relay…"
                            self.reconcileCompanion()
                        }
                        return
                    }
                } catch {
                    print("[companion] discovery poll failed: \(error.localizedDescription)")
                }
            }
        }
    }

    func unpairCompanion() {
        guard let session, !session.accessToken.isEmpty else {
            companionStatusText = "Sign in to unpair."
            return
        }
        companionDiscoveryTask?.cancel()
        companionDiscoveryTask = nil
        let pairingId = companionPairingId
        Task { [weak self] in
            guard let self else { return }
            await self.revokeCompanionPairing(accessToken: session.accessToken, pairingId: pairingId)
        }
    }

    /// Revokes the hosted pairing and stops the relay. Used by Unpair and by a real
    /// app quit so the phone cannot auto-reconnect after the desktop is gone.
    func revokeCompanionPairing(accessToken: String, pairingId: String) async {
        do {
            if !pairingId.isEmpty {
                try await backend.revokePairing(accessToken: accessToken, pairingId: pairingId)
            }
            companionPairingId = ""
            companionEnabled = false
            await companion?.stop()
            companionStatusText = "Unpaired."
        } catch {
            companionPairingId = ""
            companionEnabled = false
            await companion?.stop()
            companionStatusText = "Unpair failed. Try again."
            print("[companion] unpair failed: \(error.localizedDescription)")
        }
    }

    func reconcileCompanion() {
        guard let session else { return }
        let enabled = companionEnabled
        let pairingId = companionPairingId
        let token = session.accessToken
        Task { [weak self] in
            await self?.companion?.reconcile(enabled: enabled, pairingId: pairingId, accessToken: token)
        }
    }

    var selectedProvider: ManagedProvider? {
        providers.first(where: { $0.providerId == selectedProviderId })
            ?? byoProviders.first(where: { $0.providerId == selectedProviderId })
    }

    var byoProviderChoices: [ManagedProvider] {
        byoProviders
    }

    var byoModelChoices: [ManagedModel] {
        byoProviderChoices.first(where: { $0.providerId == selectedProviderId })?.models ?? []
    }

    var selectedSpeechProvider: ManagedProvider? { speechProviders.first(where: { $0.providerId == selectedSpeechProviderId }) }
    var usesManagedSpeech: Bool { !useBYOProvider }
    var usesCloudSpeech: Bool { usesManagedSpeech || (useBYOProvider && hasBYOEntitlement && speechRecognitionMode == "Cloud") }
    private var isByoSpeechEligible: Bool { hasBYOEntitlement }

    var protectionStatus: String {
        ProcessInfo.processInfo.operatingSystemVersion.majorVersion < 15
            ? "AppKit window exclusion enabled — verify in the meeting preview"
            : "Legacy exclusion requested — macOS 15+ does not guarantee omission"
    }

    var selectedModelSupportsVision: Bool {
        selectedProvider?.models.first(where: { $0.modelId == selectedModelId })?.supportsVision == true
    }

    var attachedScreenshotCount: Int { attachedScreenshots.count }

    var hasConfiguredBYOKeys: Bool {
        byoProviders.contains(where: { !rotation.keys(for: $0.providerId).isEmpty })
            || !rotation.keys(for: selectedProviderId).isEmpty
    }

    var isFreeTrialAccount: Bool { AccountAccess.isFree(account?.accessTier) }

    var isPremiumAccount: Bool { account?.accessTier.lowercased() == "premium" }

    var accountTypeLabel: String {
        switch account?.accessTier.lowercased() {
        case "free": return "FREE"
        case "pro_byo": return "PRO"
        case "premium": return "PREMIUM"
        default: return account?.accessTier.uppercased() ?? "ACCOUNT"
        }
    }

    var hasPremiumManagedEntitlement: Bool {
        AccountAccess.hasPremium(
            tier: account?.accessTier,
            credits: account?.wallet.premiumAvailableCredits ?? 0
        )
    }

    var hasBYOEntitlement: Bool {
        AccountAccess.hasBYO(
            tier: account?.accessTier,
            credits: account?.wallet.proAvailableCredits ?? 0
        )
    }

    var hasBothPaidLanes: Bool { hasPremiumManagedEntitlement && hasBYOEntitlement }
    var canViewDiagnostics: Bool { account?.canUseDesktopPowerFeatures == true }

    var knowledgeBaseStatus: String {
        guard isPremiumAccount, let knowledge = hostedKnowledgeBase else {
            return "Knowledge Base is available only to Premium accounts."
        }
        guard !knowledge.knowledgeBaseId.isEmpty else {
            return "No hosted Knowledge Base is linked. Create one from the Phantom dashboard."
        }
        if knowledge.canUseInInterview {
            return "\(knowledge.name): ready • \(knowledge.documentCount) documents • \(knowledge.chunkCount) chunks"
        }
        return knowledge.blockedReason.isEmpty
            ? "\(knowledge.name): \(knowledge.status)"
            : "\(knowledge.name): \(knowledge.blockedReason)"
    }

    var voicePermissionStatus: String { speechInput.permissionSummary }

    var estimatedTokens: Int {
        messages.reduce(0) { $0 + max(1, $1.content.count / 4) }
    }

    var creditStatus: String {
        guard let account else { return "Cr n/a" }
        if isFreeTrialAccount { return "Trial 2×15m" }
        return "P \(decimal(account.wallet.premiumAvailableCredits)) | B \(decimal(account.wallet.proAvailableCredits)) | D \(decimal(account.wallet.premiumNegativeCredits))"
    }

    var activeCreditLane: String {
        isFreeTrialAccount ? "TRIAL" : (useBYOProvider ? "BYO" : (allowPaidSessionExtension && (account?.wallet.premiumAvailableCredits ?? 0) <= 0 ? "DEBT" : "PREMIUM"))
    }
    var activeKeyPosition: String { useBYOProvider ? rotation.keyPosition(provider: selectedProviderId) : "" }

    /// Credit-mode chip: show BYO when BYO credits exist even if keys are missing (never Idle in that case).
    var activeCreditModeLabel: String {
        guard let account else { return "Idle" }
        if isFreeTrialAccount { return "Trial" }
        if account.wallet.premiumNegativeCredits > 0 { return "Debt" }
        if useBYOProvider || account.wallet.proAvailableCredits > 0 { return "BYO" }
        if hasPremiumManagedEntitlement { return "Premium" }
        if hasBYOEntitlement { return "BYO" }
        return "Idle"
    }

    var contextPackHasUnsavedChanges: Bool {
        guard let pack = contextPacks.first(where: { $0.packId == selectedContextPackId }) else { return false }
        return pack.name != contextPackName || pack.resumeText != resumeText || pack.jobDescriptionText != jobDescriptionText
    }

    var resumeWordCount: Int { resumeText.split(whereSeparator: { $0.isWhitespace }).count }
    var jobDescriptionWordCount: Int { jobDescriptionText.split(whereSeparator: { $0.isWhitespace }).count }
    var resumeOverLimit: Bool { resumeWordCount > Self.maxResumeWords }
    var jobDescriptionOverLimit: Bool { jobDescriptionWordCount > Self.maxJobDescriptionWords }

    func bootstrap() {
        Diagnostics.log("bootstrap:start")
        onWindowPreferencesChanged?(opacity, clickThrough, useFakeCursor, fakeCursorScale)
        if let error = runtime.storageFailure {
            Diagnostics.log("bootstrap:storage_unavailable code=storage_unavailable")
            status = error
            launchContext = AppLaunchContext(state: .backendUnavailable, title: "Read-Only Safe Mode", message: error, canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
            return
        }
        if let error = configuration.validationError {
            Diagnostics.log("bootstrap:configuration_invalid code=configuration_invalid")
            status = error
            launchContext = AppLaunchContext(state: .backendUnavailable, title: "Configuration Error", message: error, canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
            return
        }
        guard let saved = SessionStore.load() else {
            Diagnostics.log("bootstrap:no_saved_session")
            clickThrough = false
            status = "Sign in with your Phantom account."
            return
        }
        guard saved.isAuthenticated else {
            Diagnostics.log("bootstrap:saved_session_invalid")
            SessionStore.clear()
            AccountSnapshotStore.clear()
            status = "Your saved session is no longer authenticated. Sign in again."
            return
        }

        isBusy = true
        Task {
            defer { isBusy = false }
            do {
                var authenticated = saved
                if saved.expiresAtUtc == nil || saved.expiresAtUtc! <= Date().addingTimeInterval(120) {
                    Diagnostics.log("bootstrap:refreshing_session")
                    authenticated = try await backend.refresh(saved, device: device)
                    try SessionStore.save(authenticated)
                }
                try await enterApp(with: authenticated)
            } catch BackendError.http(401, _) {
                do {
                    let refreshed = try await backend.refresh(saved, device: device)
                    try SessionStore.save(refreshed)
                    try await enterApp(with: refreshed)
                } catch {
                    Diagnostics.log("bootstrap:session_refresh_failed code=refresh_failed")
                    await handleAuthenticationFailure("Your saved session expired. Sign in again.")
                }
            } catch {
                do {
                    guard let cached = try AccountSnapshotStore.load(), cached.userId == saved.userId else { throw error }
                    Diagnostics.log("bootstrap:using_cached_snapshot")
                    try await enterApp(with: saved, cachedSnapshot: cached)
                } catch {
                    Diagnostics.log("bootstrap:failed code=startup_failed")
                    launchContext = AppLaunchContext(state: .backendUnavailable, title: "Backend Unavailable", message: "Reconnect and press Retry. Offline launch requires a valid cached lease or resumable interview.", canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
                    status = launchContext.message
                }
            }
        }
    }

    func login() {
        let cleanEmail = email.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !cleanEmail.isEmpty, !password.isEmpty else {
            status = "Email and password are required."
            return
        }

        isBusy = true
        status = "Authenticating with Phantom…"
        Diagnostics.log("login:start")
        Task {
            defer { isBusy = false }
            do {
                let authenticated = try await backend.login(email: cleanEmail, password: password, device: device)
                try SessionStore.save(authenticated)
                password = ""
                try await enterApp(with: authenticated)
            } catch {
                Diagnostics.log("login:failed code=authentication_failed")
                status = error.localizedDescription
            }
        }
    }

    func register() {
        guard let base = URL(string: configuration.websiteBaseUrl)?.appendingPathComponent("register"),
              var components = URLComponents(url: base, resolvingAgainstBaseURL: false) else { return }
        components.queryItems = [
            URLQueryItem(name: "source", value: "mac_desktop"),
            URLQueryItem(name: "appVersion", value: AppVersion.current),
            URLQueryItem(name: "installId", value: device.installId),
            URLQueryItem(name: "deviceLabel", value: device.label),
            URLQueryItem(name: "deviceFingerprintHash", value: device.fingerprint),
            URLQueryItem(name: "secretFingerprintHint", value: device.secretHint)
        ]
        if let url = components.url { NSWorkspace.shared.open(url) }
    }

    func showSettings() {
        guard session != nil else { return }
        settingsClickThrough = clickThrough
        settingsSnapshot = SettingsSnapshot(
            provider: selectedProviderId, model: selectedModelId, mode: copilotMode, style: interviewDeliveryStyle,
            resume: resumeText, job: jobDescriptionText, opacity: opacity, fakeCursor: useFakeCursor,
            fakeCursorScale: fakeCursorScale,
            freeExtension: allowFreeTrialSessionExtension, paidExtension: allowPaidSessionExtension,
            autoPause: autoPauseOnInactivity, inactivity: inactivityMinutes,
            preferBYO: preferBYOCreditsFirst, voice: voiceEnabled, autoVoice: autoSendAfterVoiceStop,
            speechMode: speechRecognitionMode, speechProvider: selectedSpeechProviderId, speechModel: selectedSpeechModelId,
            speechLanguage: speechLanguage, sharedSpeechKeys: useChatKeysForSpeech, speechFallback: autoFallbackToNativeSpeech,
            legacyPath: legacyAppPath, debug: debugModeEnabled, simulation: debugErrorSimulation
        )
        if canViewDiagnostics { refreshDiagnostics() }
        screen = .settings
    }

    func closeSettings() {
        saveSettings()
    }

    func saveSettings() {
        if resumeOverLimit || jobDescriptionOverLimit {
            status = "Resume is limited to \(Self.maxResumeWords) words and job description to \(Self.maxJobDescriptionWords). Shorten them before saving."
            return
        }
        let savedClickThrough = settingsClickThrough
        if let old = settingsSnapshot, old.resume != resumeText || old.job != jobDescriptionText {
            conversationManager.reset()
            messages.removeAll()
            ConversationStore.clear()
        }
        settingsSnapshot = nil
        refreshManagedCatalog()
        screen = .chat
        clickThrough = savedClickThrough
        reconcileCompanion()
    }

    func cancelSettings() {
        guard let old = settingsSnapshot else { screen = .chat; return }
        selectedProviderId = old.provider; selectedModelId = old.model; copilotMode = old.mode; interviewDeliveryStyle = old.style
        resumeText = old.resume; jobDescriptionText = old.job; opacity = old.opacity; useFakeCursor = old.fakeCursor
        fakeCursorScale = old.fakeCursorScale
        allowFreeTrialSessionExtension = old.freeExtension; allowPaidSessionExtension = old.paidExtension
        autoPauseOnInactivity = old.autoPause; inactivityMinutes = old.inactivity
        preferBYOCreditsFirst = old.preferBYO; voiceEnabled = old.voice; autoSendAfterVoiceStop = old.autoVoice
        speechRecognitionMode = old.speechMode; selectedSpeechProviderId = old.speechProvider; selectedSpeechModelId = old.speechModel
        speechLanguage = old.speechLanguage; useChatKeysForSpeech = old.sharedSpeechKeys; autoFallbackToNativeSpeech = old.speechFallback
        legacyAppPath = old.legacyPath; debugModeEnabled = old.debug; debugErrorSimulation = old.simulation
        settingsSnapshot = nil
        screen = .chat
    }

    func correctMermaidSyntax(_ source: String) async throws -> String {
        guard !isSending else { throw BackendError.server("Wait for the current response to finish.") }
        guard let session, !selectedProviderId.isEmpty, !selectedModelId.isEmpty else {
            throw BackendError.server("AI access is not ready.")
        }
        Diagnostics.log("mermaid:correction:start chars=\(source.count)")
        let response = try await contextSummaryResponse(
            session: session,
            provider: selectedProviderId,
            model: selectedModelId,
            usesBYO: useBYOProvider,
            messages: [
                ChatMessage(role: "system", content: "You repair Mermaid syntax only. Preserve every node, label, edge, direction, and meaning. Return only corrected Mermaid source without Markdown fences or explanation."),
                ChatMessage(role: "user", content: "Correct this Mermaid code without changing its content or design:\n\n\(source)")
            ]
        )
        let corrected = Self.extractCorrectedMermaidSource(response)
        guard !corrected.isEmpty else { throw BackendError.server("The model did not return Mermaid source.") }
        Diagnostics.log("mermaid:correction:success chars=\(corrected.count)")
        return corrected
    }

    static func extractCorrectedMermaidSource(_ response: String) -> String {
        let value = response.trimmingCharacters(in: .whitespacesAndNewlines)
        if let opening = value.range(of: "```mermaid", options: .caseInsensitive),
           let closing = value.range(of: "```", options: [], range: opening.upperBound..<value.endIndex) {
            return String(value[opening.upperBound..<closing.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        if value.hasPrefix("```"), let firstLine = value.firstIndex(of: "\n"),
           let closing = value.range(of: "```", options: .backwards, range: firstLine..<value.endIndex) {
            return String(value[value.index(after: firstLine)..<closing.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines)
        }
        return value.lowercased().hasPrefix("mermaid\n")
            ? String(value.dropFirst(8)).trimmingCharacters(in: .whitespacesAndNewlines)
            : value
    }

    func retryStartup() { bootstrap() }

    func chooseLegacyApp() {
        guard account?.canUseDesktopPowerFeatures == true else { return }
        let panel = NSOpenPanel()
        panel.title = "Select Legacy Phantom App"
        panel.canChooseDirectories = true
        panel.canChooseFiles = false
        panel.allowsMultipleSelection = false
        panel.sharingType = .none
        if panel.runModal() == .OK, let url = panel.url { legacyAppPath = url.path }
    }

    func launchLegacyApp() {
        guard account?.canUseDesktopPowerFeatures == true else { return }
        let url = URL(fileURLWithPath: legacyAppPath)
        guard !legacyAppPath.isEmpty,
              FileManager.default.fileExists(atPath: url.path),
              url.pathExtension.lowercased() == "app" else {
            status = "Choose a valid macOS .app bundle in Settings first."
            return
        }
        NSWorkspace.shared.openApplication(at: url, configuration: .init()) { [weak self] _, error in
            Task { @MainActor in
                if let error { self?.status = "Legacy app launch failed: \(error.localizedDescription)" }
                else { self?.onLegacyHandoff?() }
            }
        }
    }

    func refreshDiagnostics() {
        guard canViewDiagnostics else { diagnosticsText = ""; return }
        Task {
            let queue = await runtime.diagnostics()
            let deadLetters = queue.deadLetters.map { "\($0.recordId): \($0.lastError)" }.joined(separator: "\n")
            diagnosticsText = "Usage pending: \(queue.pendingUsage) | failed: \(queue.failedUsage) | dead-letter: \(queue.deadLetters.count)\nTelemetry queued: \(queue.queuedTelemetry) | dropped: \(queue.droppedTelemetry)\nLive Copilot: \(conversationManager.lastTrace)\n\(deadLetters)\n\n\(Diagnostics.text())"
        }
    }
    func clearDiagnostics() {
        guard canViewDiagnostics else { return }
        Diagnostics.clear()
        diagnosticsText = Diagnostics.text()
    }

    func copyChat() {
        let timestamp = ISO8601DateFormatter()
        let transcript = messages.filter { !$0.content.isEmpty }.map { message in
            var metadata = message.role == "assistant" ? "PHANTOM" : "YOU"
            if let createdAtUtc = message.createdAtUtc { metadata += " • \(timestamp.string(from: createdAtUtc))" }
            if message.role == "assistant", let responseTime = message.responseTimeText {
                metadata += " • Response time: \(responseTime)"
            }
            return "\(metadata)\n\(message.content)"
        }.joined(separator: "\n\n")
        guard !transcript.isEmpty else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(transcript, forType: .string)
        status = "Chat copied with timings."
    }

    func selectContextPack(_ packId: String) {
        selectedContextPackId = packId
        guard let pack = contextPacks.first(where: { $0.packId == packId }) else {
            contextPackName = ""
            return
        }
        let changed = resumeText != pack.resumeText || jobDescriptionText != pack.jobDescriptionText
        contextPackName = pack.name
        resumeText = pack.resumeText
        jobDescriptionText = pack.jobDescriptionText
        if changed {
            conversationManager.reset()
            messages.removeAll()
            ConversationStore.clear()
        }
        contextPackStatus = "Loaded \(pack.name)."
    }

    func saveContextPack() {
        guard let session, account?.accessTier.lowercased() == "premium" else {
            contextPackStatus = "Hosted context packs require a Premium account."
            return
        }
        let name = contextPackName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else {
            contextPackStatus = "Enter a context pack name."
            return
        }
        Task {
            do {
                let saved = try await backend.saveContextPack(
                    accessToken: session.accessToken,
                    packId: selectedContextPackId,
                    name: name,
                    resumeText: resumeText,
                    jobDescriptionText: jobDescriptionText
                )
                await loadContextPacks(selecting: saved.packId)
                contextPackStatus = "Saved \(saved.name)."
            } catch {
                contextPackStatus = error.localizedDescription
            }
        }
    }

    func resetContextPackEdits() {
        guard !selectedContextPackId.isEmpty else { return }
        selectContextPack(selectedContextPackId)
        contextPackStatus = "Changes reset to the last hosted version."
    }

    func deleteContextPack() {
        guard let session, !selectedContextPackId.isEmpty else { return }
        let deleting = selectedContextPackId
        Task {
            do {
                try await backend.deleteContextPack(accessToken: session.accessToken, packId: deleting)
                selectedContextPackId = ""
                contextPackName = ""
                await loadContextPacks()
                contextPackStatus = "Context pack deleted."
            } catch {
                contextPackStatus = error.localizedDescription
            }
        }
    }

    func toggleClickThrough() {
        clickThrough.toggle()
    }

    func toggleCompact() {
        isCompact.toggle()
        onCompactModeChanged?(isCompact)
    }

    func showScreenshotPreview() {
        guard !attachedScreenshots.isEmpty else { return }
        isScreenshotPreviewVisible = true
    }

    func closeScreenshotPreview() {
        isScreenshotPreviewVisible = false
    }

    func requestVoicePermissions() {
        Task { _ = await speechInput.requestPermissions() }
    }

    func openMicrophoneSettings() { speechInput.openMicrophoneSettings() }
    func openSpeechSettings() { speechInput.openSpeechSettings() }
    func openScreenRecordingSettings() { ScreenshotCapture.openSettings() }

    func saveBYOKey() {
        guard hasBYOEntitlement else {
            byoKeyStatus = "Upgrade to Pro BYO before adding provider keys."
            return
        }
        let clean = byoAPIKey.trimmingCharacters(in: .whitespacesAndNewlines)
        let second = byoSecondAPIKey.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !selectedProviderId.isEmpty, !clean.isEmpty else {
            byoKeyStatus = "Select a provider and enter its API key."
            return
        }
        do {
            try rotation.save(provider: selectedProviderId, keys: [clean, second])
            byoAPIKey = clean
            byoSecondAPIKey = second
            byoKeyStatus = "\(selectedProviderId): \(second.isEmpty ? 1 : 2) key(s) saved in Keychain."
            syncRuntimeLane()
            refreshBYOCatalogs(forceAll: true)
        } catch {
            byoKeyStatus = error.localizedDescription
        }
    }

    func removeBYOKey() {
        guard !selectedProviderId.isEmpty else { return }
        rotation.remove(provider: selectedProviderId)
        byoAPIKey = ""
        byoSecondAPIKey = ""
        byoKeyStatus = "\(selectedProviderId) key removed."
        syncRuntimeLane()
    }

    func clearChat() {
        let auth = session
        let snapshot = account
        sessionCleanupTask = Task { await finishInterview(auth: auth, account: snapshot) }
        chatTask?.cancel()
        heartbeatTask?.cancel()
        heartbeatTask = nil
        rotation.resetConversation()
        conversationManager.reset()
        speechInput.stop()
        isSending = false
        isListening = false
        attachedScreenshots = []
        isScreenshotPreviewVisible = false
        messages.removeAll()
        resumeText = ""
        jobDescriptionText = ""
        ConversationStore.clear()
    }

    func startNewTopic() {
        let auth = session
        let snapshot = account
        sessionCleanupTask = Task { await finishInterview(auth: auth, account: snapshot) }
        chatTask?.cancel()
        heartbeatTask?.cancel()
        heartbeatTask = nil
        rotation.resetConversation()
        conversationManager.reset()
        speechInput.stop()
        isSending = false
        isListening = false
        attachedScreenshots = []
        isScreenshotPreviewVisible = false
        messages.removeAll()
        jobDescriptionText = ""
        ConversationStore.clear()
        status = "New topic — resume preserved"
    }

    func regenerateLastResponse() {
        guard !isSending,
              let index = messages.lastIndex(where: { $0.role == "user" }) else { return }
        let question = messages[index].content
        messages.removeSubrange(index...)
        ConversationStore.save(messages)
        prompt = question
        send()
    }

    func refreshBYOModels() {
        refreshBYOCatalogs(forceAll: true)
    }

    private func applyBYOCatalog(_ catalog: ManagedCatalog) {
        // Use catalog as returned (eligibleForChat only). Empty models stay empty — no hardcoded fallback.
        byoProviders = catalog.providers.map { provider in
            ManagedProvider(
                providerId: provider.providerId,
                label: provider.label.isEmpty ? provider.providerId : provider.label,
                models: provider.models.filter(\.eligibleForChat),
                refreshedAtUtc: provider.refreshedAtUtc
            )
        }
        // Always publish so Settings / chat pickers refresh even if lane flags race.
        objectWillChange.send()
        if useBYOProvider {
            self.providers = byoProviders
        }
        ensureValidChatSelection(preferConfiguredKeys: false)
    }

    private func refreshBYOCatalogs(forceProvider: String? = nil, forceAll: Bool = false) {
        guard hasBYOEntitlement, let session else {
            byoKeyStatus = "Sign in with a Pro BYO account to refresh models."
            status = byoKeyStatus
            return
        }

        Task {
            let cached = BYOCatalogStore.load()
            let coldStart = cached == nil || cached!.providers.isEmpty
            let explicit = forceAll || forceProvider != nil
            let autoAllowed = CatalogRefreshQuota.canAutoRefresh()
            // Empty models after a successful fetch are valid — only age / missing cache is stale.
            let shouldFetch = explicit || coldStart || (autoAllowed && BYOCatalogStore.isStale())
            guard shouldFetch else {
                if let cached { applyBYOCatalog(cached) }
                return
            }

            if explicit {
                byoKeyStatus = "Refreshing BYO models…"
                status = byoKeyStatus
            }

            do {
                let catalog: ManagedCatalog
                if explicit || coldStart || BYOCatalogStore.isStale() {
                    catalog = try await backend.refreshByoCatalog(
                        accessToken: session.accessToken,
                        providerId: forceAll ? "" : (forceProvider ?? "")
                    )
                } else {
                    catalog = try await backend.byoCatalog(accessToken: session.accessToken)
                }
                BYOCatalogStore.save(catalog)
                applyBYOCatalog(catalog)
                if !explicit { CatalogRefreshQuota.recordAutoRefresh() }

                if let speechCatalog = try? await backend.speechCatalog(accessToken: session.accessToken) {
                    SpeechCatalogStore.save(speechCatalog)
                    let next = speechCatalog.providers.filter { !$0.models.isEmpty }
                    if next != speechProviders {
                        speechProviders = next
                        selectSpeechModel()
                    }
                }

                if catalog.providers.allSatisfy({ $0.models.isEmpty }) {
                    byoKeyStatus = "BYO providers loaded, but models are still empty. Tap Refresh models."
                } else if explicit {
                    byoKeyStatus = "BYO chat and speech catalogs refreshed."
                }
                if explicit { status = byoKeyStatus }
            } catch {
                // Keep previous cache; never apply hardcoded BYOCatalog on failure.
                await runtime.track(
                    category: "ai",
                    event: "byo_catalog_refresh_failed",
                    attributes: ["provider": forceProvider ?? "all", "error": String(error.localizedDescription.prefix(300))],
                    accessToken: session.accessToken
                )
                byoKeyStatus = "BYO model refresh failed. Tap Refresh models to retry."
                status = byoKeyStatus
                Diagnostics.log("byo_catalog_refresh_failed code=\(String(describing: type(of: error))) detail=\(String(error.localizedDescription.prefix(200)))")
            }
        }
    }

    func captureScreenshot() {
        guard selectedModelSupportsVision else {
            status = "Choose a vision-capable model before attaching a screenshot."
            return
        }
        guard attachedScreenshots.count < Self.maxAttachedScreenshots else {
            status = "You can attach up to \(Self.maxAttachedScreenshots) screenshots."
            return
        }
        guard let onCaptureScreenshot else {
            status = "Screenshot capture is unavailable."
            return
        }

        isCapturingScreenshot = true
        status = "Capturing display…"
        Task {
            defer { isCapturingScreenshot = false }
            do {
                let data = try await onCaptureScreenshot()
                guard attachedScreenshots.count < Self.maxAttachedScreenshots else {
                    status = "You can attach up to \(Self.maxAttachedScreenshots) screenshots."
                    return
                }
                attachedScreenshots.append(data)
                status = "Screenshot \(attachedScreenshots.count)/\(Self.maxAttachedScreenshots) attached"
            } catch {
                status = error.localizedDescription
            }
        }
    }

    func removeScreenshot(at index: Int? = nil) {
        if let index, attachedScreenshots.indices.contains(index) {
            attachedScreenshots.remove(at: index)
        } else {
            attachedScreenshots = []
        }
        if attachedScreenshots.isEmpty { isScreenshotPreviewVisible = false }
        status = attachedScreenshots.isEmpty
            ? "Screenshots removed"
            : "Screenshot removed • \(attachedScreenshots.count)/\(Self.maxAttachedScreenshots) remaining"
        announceCompanionRuntime()
    }

    func companionProviderCatalog() -> [[String: Any]] {
        let list = useBYOProvider && hasBYOEntitlement ? byoProviderChoices : providers
        return list.map { provider in
            let chatModels = provider.models.filter(\.eligibleForChat)
            let models = chatModels.isEmpty ? provider.models : chatModels
            return [
                "id": provider.providerId,
                "name": provider.label,
                "models": models.map { model in
                    [
                        "id": model.modelId,
                        "name": model.displayName,
                        "vision": model.supportsVision
                    ] as [String: Any]
                }
            ]
        }
    }

    func companionAttachmentPayloads() -> [[String: Any]] {
        attachedScreenshots.enumerated().map { index, data in
            var item: [String: Any] = ["index": index]
            if let thumb = ScreenshotCapture.captureCompletedPayload(from: data, maxEdge: 480)?.thumbnailJpegBase64 {
                item["thumbnailJpegBase64"] = thumb
            }
            return item
        }
    }

    func announceCompanionRuntime() {
        guard companionEnabled else { return }
        Task { await companion?.announceReady() }
    }

    func toggleVoiceInput() {
        guard voiceEnabled else {
            voiceStatus = "Enable voice input in Settings"
            return
        }

        if speechInput.isListening {
            if autoSendAfterVoiceStop {
                pendingVoiceAutoSend = true
                voiceStatus = "Finishing transcription…"
                startVoiceCaptureSafetyTimeout()
            } else {
                pendingVoiceAutoSend = false
                voiceCaptureSafetyTask?.cancel()
            }
            speechInput.stop()
            isListening = false
        } else {
            configureSpeechRuntime()
            let existing = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
            voicePromptPrefix = existing.isEmpty ? "" : existing + " "
            previousVoiceTranscript = ""
            lastVoiceRenderedPrompt = prompt
            preserveVoiceEdits = false
            pendingVoiceTurnId = UUID().uuidString
            lastAutoSentVoiceText = ""
            pendingVoiceAutoSend = false
            voiceCaptureSafetyTask?.cancel()
            Task {
                await speechInput.start()
                isListening = speechInput.isListening
            }
        }
    }

    func send() {
        if isSending {
            cancelCurrentRequest()
            Task { try? await Task.sleep(nanoseconds: 100_000_000); send() }
            return
        }
        var text = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty, !attachedScreenshots.isEmpty { text = "Please analyze this screenshot." }
        guard !text.isEmpty,
              !isSending,
              let session,
              !selectedProviderId.isEmpty,
              !selectedModelId.isEmpty else { return }
        guard launchContext.canStartInterview || launchContext.canResumeLockedInterview else {
            status = launchContext.message
            return
        }
        let extensionEnabled = isFreeTrialAccount ? allowFreeTrialSessionExtension : allowPaidSessionExtension
        let startingPaidExtension = sessionExtensionOptInRequired && extensionEnabled && !isFreeTrialAccount
        if sessionExtensionOptInRequired, !extensionEnabled {
            status = isFreeTrialAccount ? "Enable free-trial extension in Settings to continue." : "Enable paid extension in Settings to continue."
            return
        }
        if extensionEnabled { sessionExtensionOptInRequired = false }
        if startingPaidExtension {
            runtimeLaneOverride = false
            syncRuntimeLane()
        }
        if !attachedScreenshots.isEmpty, !selectedModelSupportsVision {
            removeScreenshot()
            status = "The screenshots were removed because the selected model does not support vision."
        }
        if useBYOProvider {
            if rotation.keys(for: selectedProviderId).isEmpty {
                status = "Add a \(selectedProviderId.isEmpty ? "provider" : selectedProviderId) API key in Settings before sending."
                return
            }
        }

        applyingCompanionComposer = true
        prompt = ""
        applyingCompanionComposer = false
        lastMirroredComposer = ""
        for index in messages.indices {
            messages[index].clarificationOptions = nil
        }
        companionVoiceTranscriptHandler?("", true, true)
        let imagesBase64 = attachedScreenshots.map { $0.base64EncodedString() }
        let provider = selectedProviderId
        let model = selectedModelId
        let usesBYO = useBYOProvider
        activeExecutionLane = usesBYO ? "byo" : "managed"
        activeUsageSource = usesBYO
            ? "pro_byo"
            : (isFreeTrialAccount ? "free_trial_managed" : (startingPaidExtension ? "premium_debt_extension" : "premium_managed"))
        let requestId = pendingVoiceTurnId ?? UUID().uuidString
        pendingVoiceTurnId = nil
        activeRequestId = requestId
        activeTurnId = requestId
        activeRequestStartedAt = Date()
        firstChunkRecorded = false
        isSending = true
        var companionTurnSucceeded = false
        status = copilotMode == .interview ? "Starting interview session…" : "Starting briefing session…"
        Diagnostics.event("session_started", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle)
        Diagnostics.event("request_dispatched", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["provider": provider, "model": model, "stage": "dispatch", "execution_lane": activeExecutionLane, "usage_source": activeUsageSource])
        mirrorLiveEvent("request_dispatched", turnId: requestId, fields: ["provider": provider, "model": model, "stage": "dispatch", "execution_lane": activeExecutionLane, "usage_source": activeUsageSource])

        chatTask = Task {
            defer { isSending = false }
            var reply: ChatMessage?
            do {
                await sessionCleanupTask?.value
                sessionCleanupTask = nil
                guard let account else { throw BackendError.server("Account validation is required.") }
                let activation = try await runtime.beginInterview(
                    auth: session,
                    account: account,
                    preferBYO: preferBYOCreditsFirst,
                    canUseBYO: hasBYOEntitlement,
                    canStartNewInterview: launchContext.canStartInterview || startingPaidExtension,
                    allowPaidExtension: startingPaidExtension
                )
                guard activation.allowed else {
                    let byoNeeded = usesBYO
                        || account.wallet.proAvailableCredits > 0
                        || AccountAccess.usesBYO(
                            tier: account.accessTier,
                            proCredits: account.wallet.proAvailableCredits,
                            premiumCredits: account.wallet.premiumAvailableCredits,
                            preferBYO: preferBYOCreditsFirst
                        )
                    if byoNeeded, !hasConfiguredBYOKeys {
                        throw BackendError.server("Add a \(provider.isEmpty ? "provider" : provider) API key in Settings before sending.")
                    }
                    throw BackendError.server("\(activation.title): \(activation.message)")
                }
                lastInterviewActivityAt = Date()
                startHeartbeat()
                companionInterviewActive = true
                companionSessionStartedHandler?()
                try await runtime.metering.trackQuestion(text)
                await runtime.track(
                    category: "chat",
                    event: "question_submitted",
                    attributes: ["requestId": requestId, "provider": provider, "model": model, "lane": usesBYO ? "pro_byo" : "managed"],
                    accessToken: session.accessToken
                )

                messages.append(ChatMessage(role: "user", content: text))
                let pendingReply = ChatMessage(role: "assistant", content: "")
                reply = pendingReply
                messages.append(pendingReply)
                ConversationStore.save(messages)
                if companionDeltaHandler == nil, companionEnabled {
                    companionRequestId = requestId
                    companion?.notifyDesktopOriginatedChatStarted(requestId: requestId)
                }
                status = "Understanding…"

                if isPremiumAccount, hostedKnowledgeBase?.canUseInInterview != true {
                    Diagnostics.event("context_assembly_started", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle)
                    do {
                        let refreshedKnowledge = try await backend.knowledgeBase(accessToken: session.accessToken)
                        hostedKnowledgeBase = refreshedKnowledge
                        conversationManager.warm(refreshedKnowledge)
                        Diagnostics.event("context_assembly_completed", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success"])
                    } catch {
                        Diagnostics.event("retrieval_degraded", level: "Warning", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["error_code": "kb_summary_unavailable"])
                    }
                }
                let history = Array(messages.dropLast())
                let resume = resumeSummary.isEmpty ? resumeText : resumeSummary
                let roleContext = jobDescriptionSummary.isEmpty ? jobDescriptionText : jobDescriptionSummary
                let firstOutbound = conversationManager.firstCallMessages(
                    mode: copilotMode,
                    style: interviewDeliveryStyle,
                    resume: resume,
                    roleOrMeetingContext: roleContext,
                    conversation: history,
                    modelId: model,
                    knowledgeBase: hostedKnowledgeBase
                )
                let firstOperationId = UUID().uuidString
                let repairOutbound = conversationManager.firstCallMessages(
                    mode: copilotMode,
                    style: interviewDeliveryStyle,
                    resume: resume,
                    roleOrMeetingContext: roleContext,
                    conversation: history,
                    modelId: selectedModelId,
                    knowledgeBase: hostedKnowledgeBase,
                    protocolRepair: true
                )
                let makeStream: ([ChatMessage], String, Int) -> LiveCopilotOrchestrator.ModelStream = { outbound, operationId, attempt in
                    return { onDelta, onRetryCleanup in
                        self.activeOperationId = operationId
                        Diagnostics.event("model_call_started", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["provider": provider, "model": model, "attempt": "\(attempt)", "execution_lane": self.activeExecutionLane, "usage_source": self.activeUsageSource])
                        self.mirrorLiveEvent("model_call_started", turnId: requestId, operationId: operationId, fields: ["provider": provider, "model": model, "attempt": "\(attempt)"])
                        if usesBYO {
                            do {
                                let selected = try await self.byoResponseWithRotation(
                                    provider: provider,
                                    selectedModel: self.selectedModelId,
                                    imagesBase64: imagesBase64,
                                    messages: outbound,
                                    onDelta: onDelta,
                                    onRetryCleanup: onRetryCleanup
                                )
                                return selected.response
                            } catch {
                                guard ProviderResiliencePolicy.canCrossLane(
                                    from: "byo",
                                    to: "managed_extension",
                                    explicitlyOptedIn: self.allowPaidSessionExtension,
                                    hasOutput: self.lastProviderOperationHadOutput
                                ), !self.managedProviders.isEmpty else { throw error }
                                self.status = "Switching to managed extension…"
                                let managedProvider = self.managedProviders.first?.providerId ?? provider
                                let managedSource: InterviewUsageSource = (self.account?.wallet.premiumAvailableCredits ?? 0) > 0
                                    ? .premiumManaged
                                    : .premiumDebtExtension
                                try await self.runtime.metering.trackSource(managedSource, providerId: managedProvider)
                                self.activeExecutionLane = "managed_extension"
                                self.activeUsageSource = managedSource == .premiumManaged ? "premium_managed" : "premium_debt_extension"
                                let laneFields = [
                                    "outcome": "fallback",
                                    "execution_lane": self.activeExecutionLane,
                                    "usage_source": self.activeUsageSource,
                                    "cross_lane_fallback": "true"
                                ]
                                Diagnostics.event("execution_lane_changed", level: "Warning", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: laneFields)
                                self.mirrorLiveEvent("execution_lane_changed", turnId: requestId, operationId: operationId, fields: laneFields)
                                let selected = try await self.managedResponseWithRetry(
                                    session: session,
                                    provider: managedProvider,
                                    model: self.managedProviders.first?.models.first?.modelId ?? model,
                                    allowPaidSessionExtension: true,
                                    imagesBase64: imagesBase64,
                                    messages: outbound,
                                    turnId: requestId,
                                    operationId: operationId,
                                    onDelta: onDelta,
                                    onRetryCleanup: onRetryCleanup
                                )
                                return selected.response
                            }
                        }
                        let selected = try await self.managedResponseWithRetry(
                            session: session,
                            provider: self.selectedProviderId,
                            model: self.selectedModelId,
                            allowPaidSessionExtension: self.isFreeTrialAccount ? self.allowFreeTrialSessionExtension : self.allowPaidSessionExtension,
                            imagesBase64: imagesBase64,
                            messages: outbound,
                            turnId: requestId,
                            operationId: operationId,
                            onDelta: onDelta,
                            onRetryCleanup: onRetryCleanup
                        )
                        return selected.response
                    }
                }
                if usesBYO { try await runtime.metering.trackSource(.proBYO, providerId: provider) }
                else {
                    try await runtime.metering.trackSource(
                        isFreeTrialAccount ? .freeTrialManaged : (startingPaidExtension ? .premiumDebtExtension : .premiumManaged),
                        providerId: provider
                    )
                }
                let orchestrator = LiveCopilotOrchestrator()
                var firstProtocolAttempt = 0
                let firstStream: LiveCopilotOrchestrator.ModelStream = { onDelta, onRetryCleanup in
                    firstProtocolAttempt += 1
                    let outbound = firstProtocolAttempt == 1 ? firstOutbound : repairOutbound
                    return try await makeStream(outbound, firstOperationId, firstProtocolAttempt)(onDelta, onRetryCleanup)
                }
                let result = try await orchestrator.execute(
                    allowedEntityIds: CopilotPrompt.entityIds(hostedKnowledgeBase, mode: copilotMode),
                    allowedDocumentIds: CopilotPrompt.documentIds(hostedKnowledgeBase, mode: copilotMode),
                    questionText: text,
                    retrievalAvailable: isPremiumAccount && hostedKnowledgeBase?.canUseInInterview == true,
                    hasActiveEvidence: !conversationManager.activeEvidence(for: copilotMode).isEmpty,
                    preferredDocumentsForDecision: { decision in
                        CopilotPrompt.preferredDocumentIds(
                            entityId: decision.entityId,
                            entityType: decision.entityType,
                            knowledge: self.hostedKnowledgeBase
                        )
                    },
                    firstModel: firstStream,
                    retrieve: { decision in
                        let operationId = UUID().uuidString
                        self.status = "Searching your knowledge…"
                        Diagnostics.event("retrieval_started", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle)
                        self.mirrorLiveEvent("retrieval_started", turnId: requestId, operationId: operationId)
                        guard self.isPremiumAccount, self.hostedKnowledgeBase?.canUseInInterview == true else {
                            return LiveCopilotRetrieval(status: "unavailable", snippets: [], kbRevision: "")
                        }
                        do {
                            var preferredDocuments = decision.preferredDocumentIds
                            if self.copilotMode == .briefing, preferredDocuments.isEmpty {
                                preferredDocuments = CopilotPrompt.documentIds(self.hostedKnowledgeBase, mode: .briefing)
                            }
                            if self.copilotMode == .briefing, preferredDocuments.isEmpty {
                                Diagnostics.event("retrieval_completed", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["snippet_count": "0", "retrieval_status": "unavailable"])
                                self.mirrorLiveEvent("retrieval_completed", turnId: requestId, operationId: operationId, fields: ["snippet_count": "0", "retrieval_status": "unavailable"])
                                return LiveCopilotRetrieval(status: "unavailable", snippets: [], kbRevision: "")
                            }
                            let snippets = try await self.backend.knowledgeSnippets(
                                accessToken: session.accessToken,
                                query: decision.retrievalQuery,
                                preferredDocumentIds: preferredDocuments,
                                turnId: requestId,
                                operationId: operationId
                            )
                            let bounded = Array(snippets.prefix(3))
                            Diagnostics.event("retrieval_completed", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["snippet_count": "\(bounded.count)", "retrieval_status": bounded.isEmpty ? "empty" : "found"])
                            self.mirrorLiveEvent("retrieval_completed", turnId: requestId, operationId: operationId, fields: ["snippet_count": "\(bounded.count)", "retrieval_status": bounded.isEmpty ? "empty" : "found"])
                            return LiveCopilotRetrieval(status: bounded.isEmpty ? "empty" : "found", snippets: bounded, kbRevision: "\(self.hostedKnowledgeBase?.embeddingVersion ?? 0)")
                        } catch is CancellationError { throw CancellationError() }
                        catch {
                            Diagnostics.event("retrieval_failed", level: "Warning", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["error_code": "retrieval_failed"])
                            self.mirrorLiveEvent("retrieval_failed", turnId: requestId, operationId: operationId, fields: ["outcome": "error", "error_code": "retrieval_failed"])
                            return LiveCopilotRetrieval(status: "error", snippets: [], kbRevision: "")
                        }
                    },
                    secondModel: { decision, retrieval in
                        let finalOutbound = self.conversationManager.secondCallMessages(
                            mode: self.copilotMode,
                            style: self.interviewDeliveryStyle,
                            decision: decision,
                            retrieval: retrieval,
                            resume: resume,
                            roleOrMeetingContext: roleContext,
                            conversation: history,
                            modelId: self.selectedModelId,
                            knowledgeBase: self.hostedKnowledgeBase
                        )
                        let stream = makeStream(finalOutbound, UUID().uuidString, 1)
                        return { onDelta, onRetryCleanup in
                            try await ReasoningContext.$questionType.withValue(decision.questionType) {
                                try await stream(onDelta, onRetryCleanup)
                            }
                        }
                    },
                    publish: { chunk in self.append(chunk, to: pendingReply.id) },
                    resetPublishedAttempt: { self.clearReply(pendingReply.id) },
                    protocolRejected: { code in
                        Diagnostics.event("control_frame_rejected", level: "Warning", sessionId: self.copilotSessionId, turnId: requestId, operationId: self.activeOperationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["error_code": code, "validation_outcome": "rejected"])
                        self.mirrorLiveEvent("control_frame_rejected", turnId: requestId, operationId: self.activeOperationId, fields: ["error_code": code, "validation_outcome": "rejected"])
                    },
                    decisionParsed: { decision, calls, retrieveForced in
                        let fields = [
                            "question_type": decision.questionType,
                            "intent": decision.intent,
                            "action": decision.action.rawValue,
                            "answer_basis": decision.answerBasis,
                            "confidence_bucket": decision.confidence < 0.5 ? "low" : (decision.confidence < 0.8 ? "medium" : "high"),
                            "entity_type": decision.entityType,
                            "has_entity_id": decision.entityId.isEmpty ? "false" : "true",
                            "protocol_version": "\(decision.protocolVersion)",
                            "validation_outcome": "accepted",
                            "model_call_count": "\(calls)",
                            "retrieval_status": decision.action == .retrieve ? "pending" : "not_requested",
                            "retrieve_forced": retrieveForced ? "true" : "false"
                        ]
                        Diagnostics.event("control_frame_parsed", sessionId: self.copilotSessionId, turnId: requestId, operationId: self.activeOperationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: fields)
                        self.mirrorLiveEvent("control_frame_parsed", turnId: requestId, operationId: self.activeOperationId, fields: fields)
                    }
                )
                conversationManager.complete(result, mode: copilotMode)
                if let index = messages.firstIndex(where: { $0.id == pendingReply.id }) {
                    let responseTimeMs = max(0, Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))
                    let finalized = conversationManager.finalizeAssistantResponse(messages[index].content)
                    messages[index].content = finalized.content
                    messages[index].summary = finalized.summary
                    messages[index].estimatedTokens = ConversationManager.estimate(finalized.content)
                    messages[index].hasCode = finalized.content.contains("```")
                    messages[index].answerSource = conversationManager.lastAnswerResolution.source.rawValue
                    messages[index].interviewIntent = conversationManager.lastAnswerResolution.intent.rawValue
                    messages[index].responseTimeMs = responseTimeMs
                    messages[index].createdAtUtc = Date()
                    if result.decision.action == .clarify {
                        let parsed = ClarificationOptionParser.parse(finalized.content)
                        messages[index].content = parsed.displayText
                        messages[index].summary = parsed.displayText.count > 100 ? String(parsed.displayText.prefix(100)) + "..." : parsed.displayText
                        messages[index].estimatedTokens = ConversationManager.estimate(parsed.displayText)
                        messages[index].clarificationOptions = parsed.options
                    }
                }
                Diagnostics.event("answer_completed", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success", "answer_basis": conversationManager.lastAnswerResolution.answerBasis, "question_type": conversationManager.lastAnswerResolution.questionType, "model_call": "\(conversationManager.lastModelCallCount)", "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))", "buffered_characters": "\(messages.first(where: { $0.id == pendingReply.id })?.content.count ?? 0)", "execution_lane": activeExecutionLane, "usage_source": activeUsageSource])
                try await runtime.metering.resume()
                await runtime.track(category: "billing", event: "interview_session_resumed", attributes: ["reason": "response_succeeded"], accessToken: session.accessToken)
                lastInterviewActivityAt = Date()
                await runtime.track(
                    category: "live_copilot",
                    event: "answer_completed",
                    attributes: ["session_id": copilotSessionId, "turn_id": requestId, "mode": copilotMode.rawValue, "delivery_style": interviewDeliveryStyle.rawValue, "provider": provider, "model": selectedModelId, "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))", "buffered_characters": "\(messages.first(where: { $0.id == pendingReply.id })?.content.count ?? 0)", "outcome": "success", "execution_lane": activeExecutionLane, "usage_source": activeUsageSource],
                    accessToken: session.accessToken
                )
                status = result.decision.action == .clarify ? "Needs clarification" : "Ready"
                companionTurnSucceeded = true
            } catch is CancellationError {
                status = "Request cancelled."
                Diagnostics.event("turn_cancelled", level: "Information", sessionId: copilotSessionId, turnId: requestId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "cancelled", "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))"])
            } catch {
                let failureFields = [
                    "provider": provider,
                    "model": selectedModelId,
                    "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))",
                    "outcome": "error",
                    "error_code": Self.errorCode(error),
                    "execution_lane": activeExecutionLane,
                    "usage_source": activeUsageSource
                ]
                Diagnostics.event("turn_failed", level: "Error", sessionId: copilotSessionId, turnId: requestId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: failureFields)
                if let backendError = error as? BackendError, case .http(401, _) = backendError {
                    await handleAuthenticationFailure("Your session expired. Sign in again.")
                    return
                }
                try? await runtime.metering.pause()
                await runtime.track(category: "billing", event: "interview_session_paused", attributes: ["reason": "response_failed"], accessToken: session.accessToken)
                await runtime.track(
                    category: "live_copilot",
                    event: "turn_failed",
                    attributes: failureFields.merging([
                        "session_id": copilotSessionId,
                        "turn_id": requestId,
                        "operation_id": activeOperationId,
                        "mode": copilotMode.rawValue,
                        "delivery_style": interviewDeliveryStyle.rawValue
                    ]) { current, _ in current },
                    accessToken: session.accessToken
                )
                let failureMessage: String
                if let backendError = error as? BackendError {
                    failureMessage = UserFacingText.sanitize(backendError.localizedDescription)
                } else if error is PhantomProtocolError {
                    failureMessage = "The selected AI model returned an invalid response format. Please retry or choose another model."
                } else {
                    failureMessage = "The AI provider could not complete this request. Please retry."
                }
                if let reply, let index = messages.firstIndex(where: { $0.id == reply.id }) {
                    messages[index].content = "Error: \(failureMessage)"
                    messages[index].responseTimeMs = max(0, Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))
                    messages[index].createdAtUtc = Date()
                }
                status = failureMessage
            }
            attachedScreenshots = []
            Diagnostics.event("session_ended", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle)
            ConversationStore.save(messages)
            // Notify the companion relay that the turn finished and clear the per-turn hooks.
            let succeeded = companionTurnSucceeded
            let handler = companionTurnFinishedHandler
            let desktopRequestId = companionRequestId
            companionDeltaHandler = nil
            companionTurnFinishedHandler = nil
            companionRequestId = nil
            if let handler {
                handler(succeeded)
            } else if companionEnabled, let desktopRequestId {
                companion?.notifyDesktopOriginatedTurnFinished(requestId: desktopRequestId, succeeded: succeeded)
            }
        }
    }

    func chooseClarification(_ option: ClarificationOption, on messageId: UUID) {
        guard !isSending else { return }
        if let index = messages.firstIndex(where: { $0.id == messageId }) {
            messages[index].clarificationOptions = nil
            ConversationStore.save(messages)
        }
        prompt = option.question
        send()
    }

    func cancelCurrentRequest() {
        chatTask?.cancel()
        isSending = false
        if let index = messages.lastIndex(where: { $0.role == "assistant" && $0.content.isEmpty }) {
            messages.remove(at: index)
        }
        status = "Request cancelled."
        ConversationStore.save(messages)
    }

    // MARK: - Companion lock probes (used by CompanionCommandHost)

    /// True when the desktop currently holds an active interview lock (belt-and-suspenders
    /// check for the relay's lock_missing guard). Also true when the launch context allows
    /// resuming a previously-held lock (H15) — the relay still gates every command with
    /// lock_missing, so this only avoids a false-negative for a freshly-resumed session.
    func companionHasActiveLock() async -> Bool {
        if let session = await runtime.metering.activeSession(),
           let expiry = session.lockExpiresAtUtc,
           !session.lockToken.isEmpty,
           expiry > Date() {
            return true
        }
        return launchContext.canResumeLockedInterview
    }

    /// Synchronous variant used for status mapping (desktop.hello / session.snapshot).
    /// Returns false when the runtime session isn't loaded yet; the relay still enforces
    /// lock_missing per command, so a transient false only yields a conservative "idle".
    var companionHasActiveLockSync: Bool {
        // Best-effort: rely on the launch context flag. A live lock check would require
        // an async hop; the relay's per-command lock_missing guard remains authoritative.
        launchContext.canResumeLockedInterview
    }

    func companionLockExpiresAt() async -> Date? {
        await runtime.metering.activeSession()?.lockExpiresAtUtc
    }

    /// Called by the companion orchestrator to hide/restore the overlay (spec §8.2).
    /// Forwards to the PhantomMain hook which performs the actual orderOut/showWindow.
    func setCompanionOverlayHidden(_ hidden: Bool) {
        companionOverlayHidden = hidden
        onCompanionOverlayHidden?(hidden)
    }

    /// Lightweight topic reset for companion `chat.new_topic` (C10). Resets the local
    /// conversation state WITHOUT releasing the interview lock or finishing the session
    /// — unlike startNewTopic(), which calls finishInterview and would drop the lock the
    /// phone is actively relying on.
    func startNewTopicCompanion() {
        chatTask?.cancel()
        rotation.resetConversation()
        conversationManager.reset()
        isSending = false
        isListening = false
        attachedScreenshots = []
        isScreenshotPreviewVisible = false
        messages.removeAll()
        jobDescriptionText = ""
        ConversationStore.clear()
        status = "New topic — lock preserved"
    }

    private func byoResponseWithRotation(
        provider: String,
        selectedModel: String,
        imagesBase64: [String],
        messages: [ChatMessage],
        onDelta: @escaping (String) -> Void,
        onRetryCleanup: @escaping () -> Void
    ) async throws -> (model: String, response: String) {
        let models = selectedProvider?.models.map(\.modelId) ?? [selectedModel]
        guard var key = rotation.currentKey(provider: provider) else { throw BackendError.server("No usable \(provider) API key remains.") }
        var model = rotation.model(provider: provider, models: models, selected: selectedModel)
        var triedModel = false
        var lastError: Error = BackendError.server("Provider request failed.")
        lastProviderOperationHadOutput = false

        for attempt in 1...ProviderResiliencePolicy.byoDesktopMaxAttempts {
            do {
                if debugModeEnabled {
                    debugRequestCount += 1
                    if let simulated = simulatedError(attempt: attempt, keyIndex: key.index) { throw simulated }
                }
                let modelStartedAt = Date()
                let bytes = try await byoClient.chatStream(
                    provider: provider,
                    model: model,
                    apiKey: key.value,
                    imagesBase64: imagesBase64,
                    messages: messages
                )
                Diagnostics.event("provider_headers_received", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model])
                mirrorLiveEvent("provider_headers_received", operationId: activeOperationId, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model])
                var received = false
                var response = ""
                var terminal: BYOStreamTerminal?
                var chunkCount = 0
                for try await line in bytes.lines {
                    try Task.checkCancellation()
                    if let failure = BYOClient.failure(from: line) { throw BYOError.server(status: 0, message: failure) }
                    if let delta = BYOClient.delta(from: line, provider: provider) {
                        received = true
                        lastProviderOperationHadOutput = true
                        chunkCount += 1
                        response += delta
                        onDelta(delta)
                    }
                    if let state = BYOClient.terminal(from: line, provider: provider) {
                        terminal = state
                        if state == .truncated { throw BackendError.server("The AI provider reached its output limit.") }
                        break
                    }
                }
                guard received else { throw BackendError.server("The AI provider returned an empty response.") }
                guard terminal == .complete else { throw BackendError.server("The AI provider stream ended unexpectedly.") }
                Diagnostics.event("model_call_completed", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                mirrorLiveEvent("model_call_completed", operationId: activeOperationId, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                rotation.recordSuccess(provider: provider, index: key.index)
                byoKeyStatus = "Key \(key.index + 1)/\(rotation.keys(for: provider).count) • \(model)"
                return (model, response)
            } catch {
                lastError = error
                let decision = rotation.classify(error)
                rotation.recordFailure(provider: provider, index: key.index, decision: decision)
                guard ProviderResiliencePolicy.canRetry(
                    decision,
                    attempt: attempt,
                    maxAttempts: ProviderResiliencePolicy.byoDesktopMaxAttempts,
                    hasOutput: lastProviderOperationHadOutput
                ) else { throw error }

                var keyRotated = false
                if autoSwitchKeysOnError, rotation.availableKeyCount(provider: provider) > 0,
                   let next = rotation.nextKey(provider: provider) {
                    key = next
                    keyRotated = true
                } else if decision.kind == .transient, autoSwitchModelsOnError, !triedModel,
                          let next = rotation.nextModel(provider: provider, models: models, selected: model) {
                    model = next
                    triedModel = true
                    rotation.recordSuccess(provider: provider, index: key.index)
                } else {
                    throw error
                }

                onRetryCleanup()
                let fields = [
                    "outcome": "retry", "attempt": "\(attempt + 1)", "provider": provider,
                    "model": model, "error_code": decision.kind.rawValue,
                    "retry_reason_code": decision.kind.rawValue, "key_rotated": keyRotated ? "true" : "false",
                    "execution_lane": "byo", "usage_source": "pro_byo"
                ]
                Diagnostics.event("provider_retry_started", level: "Warning", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: fields)
                mirrorLiveEvent("provider_retry_started", operationId: activeOperationId, fields: fields)
                if keyRotated {
                    Diagnostics.event("provider_rotated", level: "Warning", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "retry", "attempt": "\(attempt + 1)", "provider": provider, "model": model, "key_rotated": "true", "execution_lane": "byo", "usage_source": "pro_byo"])
                    mirrorLiveEvent("provider_rotated", operationId: activeOperationId, fields: ["outcome": "retry", "attempt": "\(attempt + 1)", "provider": provider, "model": model, "key_rotated": "true"])
                }
                status = "Retry \(attempt + 1)/\(ProviderResiliencePolicy.byoDesktopMaxAttempts) • Key #\(key.index + 1) • \(model)"
                try await Task.sleep(nanoseconds: 300_000_000)
            }
        }
        throw lastError
    }

    private func managedResponseWithRetry(
        session: AuthSession,
        provider: String,
        model: String,
        allowPaidSessionExtension: Bool,
        imagesBase64: [String],
        messages: [ChatMessage],
        turnId: String,
        operationId: String,
        onDelta: @escaping (String) -> Void,
        onRetryCleanup: @escaping () -> Void
    ) async throws -> (provider: String, model: String, response: String) {
        var authenticated = self.session ?? session
        var refreshed = false
        lastProviderOperationHadOutput = false
        while true {
            do {
                let modelStartedAt = Date()
                let bytes = try await backend.chatStream(
                    session: authenticated,
                    provider: provider,
                    model: model,
                    allowPaidSessionExtension: allowPaidSessionExtension,
                    imagesBase64: imagesBase64,
                    messages: messages,
                    turnId: turnId,
                    operationId: operationId
                )
                Diagnostics.event("provider_headers_received", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model, "execution_lane": activeExecutionLane, "usage_source": activeUsageSource])
                mirrorLiveEvent("provider_headers_received", operationId: activeOperationId, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model, "execution_lane": activeExecutionLane, "usage_source": activeUsageSource])
                var received = false
                var response = ""
                var completed = false
                var chunkCount = 0
                streamLoop: for try await line in bytes.lines {
                    try Task.checkCancellation()
                    switch SSEParser.parse(line) {
                    case .delta(let delta): received = true; lastProviderOperationHadOutput = true; chunkCount += 1; response += delta; onDelta(delta)
                    case .failure: throw BackendError.server("The managed provider stream failed.")
                    case .done: completed = true; break streamLoop
                    case nil: continue
                    }
                }
                guard received else { throw BackendError.server("The AI provider returned an empty response.") }
                guard completed else { throw BackendError.server("The managed provider stream ended unexpectedly.") }
                Diagnostics.event("model_call_completed", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                mirrorLiveEvent("model_call_completed", operationId: activeOperationId, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                return (provider, model, response)
            } catch BackendError.http(401, _) where !refreshed {
                guard !lastProviderOperationHadOutput else { throw BackendError.server("The managed provider stream ended unexpectedly.") }
                authenticated = try await backend.refresh(authenticated, device: device)
                try SessionStore.save(authenticated)
                self.session = authenticated
                refreshed = true
                onRetryCleanup()
                continue
            } catch {
                throw error
            }
        }
    }

    private func append(_ delta: String, to replyId: UUID) {
        guard let index = messages.firstIndex(where: { $0.id == replyId }) else { return }
        messages[index].content += delta
        if let handler = companionDeltaHandler {
            handler(delta)
        } else if companionEnabled, let requestId = companionRequestId {
            companion?.notifyDesktopOriginatedDelta(delta, requestId: requestId)
        }
        if !firstChunkRecorded {
            firstChunkRecorded = true
            let latency = Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000)
            Diagnostics.event("model_first_byte_received", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["elapsed_ms": "\(latency)"])
            Task { await runtime.track(category: "live_copilot", event: "model_first_byte_received", attributes: ["session_id": copilotSessionId, "turn_id": activeTurnId, "operation_id": activeOperationId, "elapsed_ms": "\(latency)"], accessToken: session?.accessToken) }
        }
        status = "Streaming response…"
    }

    private static func errorCode(_ error: Error) -> String {
        if error is CancellationError { return "cancelled" }
        if let protocolError = error as? PhantomProtocolError { return protocolError.code }
        if let urlError = error as? URLError, urlError.code == .timedOut { return "timeout" }
        if let backend = error as? BackendError, case .http(let status, _) = backend {
            if status == 429 { return "rate_limited" }
            return status > 0 ? "backend_\(status / 100)xx" : "backend_invalid_response"
        }
        return "provider_error"
    }

    private func mirrorLiveEvent(
        _ event: String,
        turnId: String? = nil,
        operationId: String = "",
        fields: [String: String] = [:]
    ) {
        var attributes = fields
        attributes["session_id"] = copilotSessionId
        attributes["turn_id"] = turnId ?? activeTurnId
        attributes["operation_id"] = operationId
        attributes["mode"] = copilotMode.rawValue
        attributes["delivery_style"] = interviewDeliveryStyle.rawValue
        attributes["execution_lane"] = activeExecutionLane
        attributes["usage_source"] = activeUsageSource
        let payload = attributes
        Task { await runtime.track(category: "live_copilot", event: event, attributes: payload, accessToken: session?.accessToken) }
    }

    private func clearReply(_ replyId: UUID) {
        guard let index = messages.firstIndex(where: { $0.id == replyId }) else { return }
        messages[index].content = ""
    }

    private func simulatedError(attempt: Int, keyIndex: Int) -> Error? {
        switch debugErrorSimulation {
        case "429": return BYOError.server(status: 429, message: "Simulated rate limit")
        case "Timeout": return URLError(.timedOut)
        case "Random": return Bool.random() ? URLError(.cannotConnectToHost) : nil
        case "Alternating keys": return keyIndex.isMultiple(of: 2) ? BYOError.server(status: 429, message: "Simulated alternating-key failure") : nil
        case "First two fail": return attempt <= 2 ? BYOError.server(status: 503, message: "Simulated transient failure") : nil
        default: return nil
        }
    }

    func logout() {
        let current = session
        let snapshot = account
        SessionStore.clear()
        AccountSnapshotStore.clear()
        chatTask?.cancel()
        heartbeatTask?.cancel()
        heartbeatTask = nil
        sessionStatusTask?.cancel()
        sessionStatusTask = nil
        speechInput.stop()
        session = nil
        clickThrough = false
        account = nil
        hostedKnowledgeBase = nil
        conversationManager.reset()
        providers = []
        contextPacks = []
        selectedContextPackId = ""
        contextPackName = ""
        messages = []
        attachedScreenshots = []
        ConversationStore.clear()
        screen = .login
        status = "Signed out."
        onLogout?()
        guard let current else { return }
        Task {
            // Stop the companion relay before tearing down auth so the phone sees a clean
            // disconnect and we don't reconnect with a stale token (H13).
            await self.companion?.stop()
            await finishInterview(auth: current, account: snapshot)
            try? await backend.logout(current)
        }
    }

    func prepareForTermination() async {
        chatTask?.cancel()
        heartbeatTask?.cancel()
        heartbeatTask = nil
        sessionStatusTask?.cancel()
        sessionStatusTask = nil
        if !preserveConversationOnTermination {
            ConversationStore.clear()
            // Real quit (not restart): drop the hosted pairing immediately so the phone
            // leaves the session without waiting for interview finalize.
            if let session, !companionPairingId.isEmpty {
                await revokeCompanionPairing(accessToken: session.accessToken, pairingId: companionPairingId)
            } else {
                await companion?.stop()
            }
        } else {
            await companion?.stop()
        }
        await finishInterview(auth: session, account: account)
    }

    func restartApp() {
        preserveConversationOnTermination = true
        ConversationStore.save(messages)
        UserDefaults.standard.set(true, forKey: "conversation.restoreAfterRestart")
        UserDefaults.standard.synchronize()
        guard onRestart?() == true else {
            preserveConversationOnTermination = false
            UserDefaults.standard.removeObject(forKey: "conversation.restoreAfterRestart")
            UserDefaults.standard.synchronize()
            status = "Phantom could not restart. This window remains open."
            Diagnostics.log("restart:launch_failed error_code=process_start_failed outcome=error")
            return
        }
        Diagnostics.log("restart:child_started outcome=success")
    }

    private func finishInterview(auth: AuthSession?, account snapshot: StartupSnapshot?) async {
        guard let snapshot else { return }
        if let completion = await runtime.finalize(
            auth: auth,
            account: snapshot,
            preferBYO: preferBYOCreditsFirst,
            canUseBYO: AccountAccess.hasBYO(
                tier: snapshot.accessTier,
                credits: snapshot.wallet.proAvailableCredits
            )
        ) {
            account?.wallet.proAvailableCredits = completion.remainingProCredits
            account?.wallet.premiumAvailableCredits = completion.remainingPremiumCredits
            account?.wallet.premiumNegativeCredits += completion.premiumDebtAdded
            if let account { try? AccountSnapshotStore.save(account) }
        }
    }

    private func startHeartbeat() {
        guard heartbeatTask == nil else { return }
        heartbeatTask = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(nanoseconds: 60_000_000_000)
                guard !Task.isCancelled, let self, let auth = self.session else { break }
                await self.runtime.heartbeat(auth: auth)
            }
        }
    }

    private func enterApp(with authenticated: AuthSession, cachedSnapshot: StartupSnapshot? = nil) async throws {
        status = "Validating account access…"
        let offline = cachedSnapshot != nil
        var snapshot: StartupSnapshot
        if let cachedSnapshot { snapshot = cachedSnapshot }
        else { snapshot = try await backend.startupCheck(authenticated) }
        launchContext = AppLaunchContext.evaluate(snapshot, offline: offline)
        Diagnostics.log("startup_snapshot offline=\(offline) tier=\(snapshot.accessTier) power_features=\(snapshot.canUseDesktopPowerFeatures) kb_status=\(snapshot.hostedKnowledgeBase.status) kb_usable=\(snapshot.hostedKnowledgeBase.canUseInInterview) kb_documents=\(snapshot.hostedKnowledgeBase.documentCount) kb_chunks=\(snapshot.hostedKnowledgeBase.chunkCount) profile=\(snapshot.hostedKnowledgeBase.profileCard != nil) experiences=\(snapshot.hostedKnowledgeBase.experienceCards?.count ?? 0) projects=\(snapshot.hostedKnowledgeBase.projectCards?.count ?? 0)")
        guard snapshot.emailVerified else {
            status = "Verify your email on the Phantom website, then sign in again."
            return
        }
        guard launchContext.canOpenApp else {
            status = launchContext.message
            return
        }

        session = authenticated
        if !offline {
            try AccountSnapshotStore.save(snapshot)
            await runtime.flush(accessToken: authenticated.accessToken)
            snapshot = (try? await backend.startupCheck(authenticated)) ?? snapshot
            try AccountSnapshotStore.save(snapshot)
            launchContext = AppLaunchContext.evaluate(snapshot, offline: false)
        }
        account = snapshot
        try await runtime.configure(snapshot: snapshot)
        await runtime.track(
            category: "auth",
            event: "desktop_session_started",
            attributes: ["platform": "macOS", "tier": snapshot.accessTier],
            accessToken: authenticated.accessToken
        )
        hostedKnowledgeBase = snapshot.hostedKnowledgeBase
        conversationManager.warm(snapshot.hostedKnowledgeBase)
        Diagnostics.log("enter_app:ready offline=\(offline) launch_state=\(launchContext.state.rawValue)")
        if let crash = Diagnostics.consumeCrash() {
            await runtime.track(category: "crash", event: "previous_unhandled_exception", attributes: ["message": String(crash.prefix(500))], accessToken: authenticated.accessToken)
        }
        if !hasBYOEntitlement {
            BYOCatalog.providers.forEach { rotation.remove(provider: $0.providerId) }
        }
        if snapshot.accessTier.lowercased() == "pro_byo",
           UserDefaults.standard.object(forKey: "chat.useBYO") == nil {
            useBYOProvider = true
        }
        email = authenticated.email
        let catalog: ManagedCatalog
        if offline {
            catalog = ManagedCatalogStore.load() ?? ManagedCatalog(providers: [])
        } else {
            catalog = try await backend.catalog(accessToken: authenticated.accessToken)
            try ManagedCatalogStore.save(catalog)
        }
        managedProviders = catalog.providers.filter { !$0.models.isEmpty }
        let speechCatalog = offline
            ? (SpeechCatalogStore.load() ?? ManagedCatalog(providers: []))
            : ((try? await backend.speechCatalog(accessToken: authenticated.accessToken)) ?? SpeechCatalogStore.load() ?? ManagedCatalog(providers: []))
        if !offline, !speechCatalog.providers.isEmpty { SpeechCatalogStore.save(speechCatalog) }
        speechProviders = speechCatalog.providers.filter { !$0.models.isEmpty }
        selectSpeechModel()
        providers = managedProviders
        selectAvailableModel()
        syncRuntimeLane()
        status = providers.isEmpty ? "No managed AI models are currently available." : launchContext.message
        screen = .chat
        startSessionStatusTimer()
        // Always hit BYO catalog on enter so Mac does not keep a stale/hardcoded local cache.
        if hasBYOEntitlement {
            refreshBYOCatalogs(forceAll: true)
        }
        if !UserDefaults.standard.bool(forKey: "conversation.restoreAfterRestart") {
            messages.removeAll()
            ConversationStore.clear()
        }
        UserDefaults.standard.removeObject(forKey: "conversation.restoreAfterRestart")
        if isPremiumAccount {
            await loadContextPacks()
        }
        // Bootstrap re-entry: reconcile companion mode after the app is ready so a
        // previously-enabled pairing reconnects its relay without a manual toggle (H14).
        reconcileCompanion()
    }

    private func selectAvailableModel() {
        ensureValidChatSelection(preferConfiguredKeys: true)
    }

    private func ensureValidChatSelection(preferConfiguredKeys: Bool) {
        if preferConfiguredKeys,
           useBYOProvider,
           rotation.keys(for: selectedProviderId).isEmpty,
           let configured = byoProviders.first(where: { !rotation.keys(for: $0.providerId).isEmpty }) {
            selectedProviderId = configured.providerId
        }
        if !providers.contains(where: { $0.providerId == selectedProviderId }) {
            selectedProviderId = providers.first?.providerId ?? ""
        }
        guard let provider = selectedProvider else {
            selectedModelId = ""
            return
        }
        let chatModels = provider.models.filter(\.eligibleForChat)
        let models = chatModels.isEmpty ? provider.models : chatModels
        if !models.contains(where: { $0.modelId == selectedModelId }) {
            selectedModelId = models.first?.modelId ?? ""
        }
        loadBYOKey()
    }

    private func ensureValidSpeechSelection() {
        guard let firstProvider = speechProviders.first else {
            if !selectedSpeechModelId.isEmpty { selectedSpeechModelId = "" }
            return
        }
        if !speechProviders.contains(where: { $0.providerId == selectedSpeechProviderId }) {
            selectedSpeechProviderId = firstProvider.providerId
            return
        }
        guard let provider = selectedSpeechProvider else { return }
        if !provider.models.contains(where: { $0.modelId == selectedSpeechModelId }) {
            selectedSpeechModelId = provider.models.first?.modelId ?? ""
        }
    }

    private func loadContextPacks(selecting preferredId: String? = nil) async {
        guard let session else { return }
        do {
            contextPacks = try await backend.contextPacks(accessToken: session.accessToken)
            if let preferredId { selectContextPack(preferredId) }
        } catch {
            contextPackStatus = error.localizedDescription
        }
    }

    private func configureSpeechInput() {
        speechInput.onTranscript = { [weak self] transcript, isFinal in
            guard let self else { return }
            let merged = Self.mergeTranscript(
                current: self.prompt,
                lastRendered: self.lastVoiceRenderedPrompt,
                previous: self.previousVoiceTranscript,
                next: transcript,
                prefix: self.voicePromptPrefix,
                preservingEdits: self.preserveVoiceEdits
            )
            self.prompt = merged.text
            self.preserveVoiceEdits = merged.preservingEdits
            self.previousVoiceTranscript = transcript
            self.lastVoiceRenderedPrompt = merged.text
            self.lastMirroredComposer = merged.text
            self.companionVoiceTranscriptHandler?(merged.text, isFinal, false)
            Diagnostics.event(
                isFinal ? "transcript_finalized" : "transcript_partial_received",
                level: isFinal ? "Information" : "Debug",
                sessionId: self.copilotSessionId,
                turnId: self.pendingVoiceTurnId ?? UUID().uuidString,
                mode: self.copilotMode,
                style: self.interviewDeliveryStyle,
                fields: ["transcript_length_bucket": Self.lengthBucket(merged.text.count)]
            )
        }
        speechInput.onCaptureCompleted = { [weak self] in
            self?.finishVoiceCaptureAndMaybeSend()
        }
        speechInput.onStateChange = { [weak self] state in
            guard let self else { return }
            self.voiceStatus = state
            self.isListening = self.speechInput.isListening
        }
    }

    func applyCompanionComposer(text: String, sent: Bool) {
        applyingCompanionComposer = true
        defer { applyingCompanionComposer = false }
        if sent {
            prompt = ""
            lastMirroredComposer = ""
            voicePromptPrefix = ""
            previousVoiceTranscript = ""
            lastVoiceRenderedPrompt = ""
            preserveVoiceEdits = false
            return
        }
        guard prompt != text else {
            lastMirroredComposer = text
            return
        }
        prompt = text
        lastMirroredComposer = text
        lastVoiceRenderedPrompt = text
        previousVoiceTranscript = ""
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        voicePromptPrefix = trimmed.isEmpty ? "" : trimmed + " "
        preserveVoiceEdits = true
    }

    private func scheduleCompanionComposerMirror() {
        guard companionVoiceTranscriptHandler != nil else { return }
        composerMirrorTask?.cancel()
        composerMirrorTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 180_000_000)
            guard !Task.isCancelled, let self else { return }
            guard !self.applyingCompanionComposer else { return }
            let text = self.prompt
            guard text != self.lastMirroredComposer else { return }
            self.lastMirroredComposer = text
            if !self.speechInput.isListening {
                self.preserveVoiceEdits = true
                self.lastVoiceRenderedPrompt = text
            }
            self.companionVoiceTranscriptHandler?(text, true, false)
        }
    }

    private func startVoiceCaptureSafetyTimeout() {
        voiceCaptureSafetyTask?.cancel()
        voiceCaptureSafetyTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: 20_000_000_000)
            guard !Task.isCancelled, let self, self.pendingVoiceAutoSend else { return }
            self.pendingVoiceAutoSend = false
            self.voiceStatus = "Ready"
            self.status = "Transcription still running — press Send when the text looks complete"
        }
    }

    private func finishVoiceCaptureAndMaybeSend() {
        voiceCaptureSafetyTask?.cancel()
        guard pendingVoiceAutoSend, !speechInput.isListening else { return }
        pendingVoiceAutoSend = false
        let text = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else {
            voiceStatus = "Ready"
            status = "No speech detected — try again"
            return
        }
        guard text != lastAutoSentVoiceText else {
            Diagnostics.event("request_dispatched", level: "Debug", sessionId: copilotSessionId, turnId: pendingVoiceTurnId ?? UUID().uuidString, mode: copilotMode, style: interviewDeliveryStyle, fields: ["duplicate_suppression_count": "1", "outcome": "suppressed"])
            return
        }
        lastAutoSentVoiceText = text
        send()
    }

    private static func lengthBucket(_ length: Int) -> String {
        switch length {
        case ...0: return "empty"
        case 1...40: return "1-40"
        case 41...160: return "41-160"
        case 161...640: return "161-640"
        default: return "641+"
        }
    }

    static func mergeTranscript(
        current: String,
        lastRendered: String,
        previous: String,
        next: String,
        prefix: String,
        preservingEdits: Bool
    ) -> (text: String, preservingEdits: Bool) {
        let edited = preservingEdits || current != lastRendered
        guard edited else { return (prefix + next, false) }
        let oldWordCount = previous.split(whereSeparator: { $0.isWhitespace }).count
        let added = next.split(whereSeparator: { $0.isWhitespace }).dropFirst(oldWordCount).joined(separator: " ")
        guard !added.isEmpty else { return (current, true) }
        return (current + (current.last?.isWhitespace == true || current.isEmpty ? "" : " ") + added, true)
    }

    private func loadBYOKey() {
        guard !selectedProviderId.isEmpty else {
            byoAPIKey = ""
            byoSecondAPIKey = ""
            return
        }
        let keys = rotation.keys(for: selectedProviderId)
        byoAPIKey = keys.first ?? ""
        byoSecondAPIKey = keys.dropFirst().first ?? ""
        syncRuntimeLane()
    }

    private func selectSpeechModel() {
        ensureValidSpeechSelection()
    }

    private func loadSpeechKeys() {
        let keys = rotation.speechKeys(provider: selectedSpeechProviderId)
        speechAPIKey = keys.first ?? ""
        speechSecondAPIKey = keys.dropFirst().first ?? ""
    }

    func saveSpeechKeys() {
        guard isByoSpeechEligible else { speechKeyStatus = "Dedicated speech keys require Pro BYO."; return }
        do {
            try rotation.saveSpeech(provider: selectedSpeechProviderId, keys: [speechAPIKey, speechSecondAPIKey])
            speechKeyStatus = "Dedicated speech keys saved."
        } catch { speechKeyStatus = error.localizedDescription }
    }

    func removeSpeechKeys() {
        rotation.removeSpeech(provider: selectedSpeechProviderId)
        speechAPIKey = ""; speechSecondAPIKey = ""; speechKeyStatus = "Dedicated speech keys removed."
    }

    private func configureSpeechRuntime() {
        guard usesCloudSpeech, let session else {
            speechInput.configureCloud(transcriber: nil, fallbackToNative: true, preferCloud: false)
            return
        }
        let managed = usesManagedSpeech
        let provider = selectedSpeechProviderId
        let model = selectedSpeechModelId
        let language = speechLanguage.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "en" : speechLanguage
        let sharedKeys = useChatKeysForSpeech
        speechInput.configureCloud(transcriber: { [weak self] pcm in
            guard let self else { throw BackendError.server("Speech service is unavailable.") }
            do {
                let text = try await self.speechClient.transcribe(
                    pcm16: pcm, session: session, managed: managed, provider: provider,
                    model: model, language: language, useChatKeys: sharedKeys)
                Diagnostics.log("speech_provider_completed route=\(managed ? "managed" : "byo") provider=\(provider) model=\(model)")
                return text
            } catch {
                let failure = self.rotation.classify(error)
                Diagnostics.log("speech_provider_failed route=\(managed ? "managed" : "byo") provider=\(provider) model=\(model) error_code=\(failure.kind.rawValue)")
                throw error
            }
        }, fallbackToNative: managed || autoFallbackToNativeSpeech, preferCloud: usesCloudSpeech)
    }

    private func syncRuntimeLane() {
        guard account != nil else { return }
        useBYOProvider = runtimeLaneOverride ?? AccountAccess.usesBYO(
            tier: account?.accessTier,
            proCredits: account?.wallet.proAvailableCredits ?? 0,
            premiumCredits: account?.wallet.premiumAvailableCredits ?? 0,
            preferBYO: preferBYOCreditsFirst
        )
        let available = useBYOProvider ? byoProviders : managedProviders
        if providers != available {
            providers = available
            selectAvailableModel()
        }
    }

    private func refreshManagedCatalog() {
        guard let session else { return }
        Task {
            do {
                let catalog = try await backend.catalog(accessToken: session.accessToken)
                try ManagedCatalogStore.save(catalog)
                managedProviders = catalog.providers.filter { !$0.models.isEmpty }
                if let speechCatalog = try? await backend.speechCatalog(accessToken: session.accessToken) {
                    SpeechCatalogStore.save(speechCatalog)
                    speechProviders = speechCatalog.providers.filter { !$0.models.isEmpty }
                    selectSpeechModel()
                }
                if !useBYOProvider { providers = managedProviders; selectAvailableModel() }
            } catch {
                Diagnostics.log("managed_catalog_refresh_failed code=catalog_unavailable")
            }
        }
    }

    private func startSessionStatusTimer() {
        sessionStatusTask?.cancel()
        sessionStatusTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { return }
                await self.refreshSessionStatus()
                try? await Task.sleep(nanoseconds: 1_000_000_000)
            }
        }
    }

    private func refreshSessionStatus() async {
        guard let account else {
            sessionTimerText = ""
            sessionStatusText = "Idle"
            return
        }
        let live = await runtime.meteringStatus(
            tier: account.accessTier,
            allowFreeTrialExtension: allowFreeTrialSessionExtension,
            allowPaidExtension: allowPaidSessionExtension
        )
        guard let active = live.session else {
            sessionTimerText = ""
            sessionStatusText = activeCreditModeLabel
            meteringSessionId = ""
            laneChargeBaseline = 0
            runtimeLaneOverride = nil
            return
        }
        if meteringSessionId != active.sessionId {
            meteringSessionId = active.sessionId
            laneChargeBaseline = 0
            runtimeLaneOverride = nil
        }
        let seconds = max(0, Int(live.elapsed))
        sessionTimerText = String(format: "%02d:%02d:%02d", seconds / 3600, seconds / 60 % 60, seconds % 60)
        sessionStatusText = "\(active.state == .paused ? "Paused" : "Live") | \(seconds / 60) min | \(decimal(live.projectedCharge)) cr"
        let laneCharge = max(0, live.projectedCharge - laneChargeBaseline)
        if !isFreeTrialAccount, useBYOProvider,
           laneCharge >= account.wallet.proAvailableCredits,
           account.wallet.premiumAvailableCredits > 0 {
            runtimeLaneOverride = false
            laneChargeBaseline = live.projectedCharge
            syncRuntimeLane()
            status = "Switched from BYO to Premium credits."
            await runtime.track(category: "billing", event: "credit_lane_switched", attributes: ["from": "pro_byo", "to": "premium"], accessToken: session?.accessToken)
        } else if !isFreeTrialAccount, !useBYOProvider,
                  laneCharge >= account.wallet.premiumAvailableCredits,
                  account.wallet.proAvailableCredits > 0,
                  byoProviders.contains(where: { !rotation.keys(for: $0.providerId).isEmpty }) {
            runtimeLaneOverride = true
            laneChargeBaseline = live.projectedCharge
            syncRuntimeLane()
            status = "Switched from Premium to BYO credits."
            await runtime.track(category: "billing", event: "credit_lane_switched", attributes: ["from": "premium", "to": "pro_byo"], accessToken: session?.accessToken)
        }
        if autoPauseOnInactivity, !isSending, active.state == .active,
           Date().timeIntervalSince(lastInterviewActivityAt) >= Double(max(10, inactivityMinutes) * 60) {
            await runtime.pauseForInactivity(accessToken: session?.accessToken)
            messages.append(ChatMessage(role: "assistant", content: "⏸️ **Interview auto-paused**\n\nNo activity was detected for \(max(10, inactivityMinutes)) minutes. Send another message to resume."))
            ConversationStore.save(messages)
            status = "Interview auto-paused"
            lastInterviewActivityAt = Date()
            return
        }
        guard live.shouldFinalize, !boundaryFinalizationInProgress else { return }
        boundaryFinalizationInProgress = true
        chatTask?.cancel()
        await finishInterview(auth: session, account: account)
        sessionExtensionOptInRequired = true
        status = live.boundaryMessage
        messages.append(ChatMessage(role: "assistant", content: "⏱️ **Interview boundary reached**\n\n\(live.boundaryMessage)"))
        ConversationStore.save(messages)
        await runtime.track(category: "billing", event: "interview_boundary_finalized", attributes: ["reason": live.boundaryMessage], accessToken: session?.accessToken)
        boundaryFinalizationInProgress = false
    }

    private func decimal(_ value: Decimal) -> String {
        NSDecimalNumber(decimal: value).stringValue
    }

    private func scheduleContextWarmup() {
        contextWarmupTask?.cancel()
        let resume = resumeText
        let job = jobDescriptionText
        contextSummaryStatus = (resume.isEmpty && job.isEmpty) ? "No resume or job description added." : "Preparing context summaries…"
        contextWarmupTask = Task { [weak self] in
            await Task.yield()
            guard !Task.isCancelled, let self else { return }
            let resumeValue = ContextSummaryStore.load(kind: "resume", source: resume)
                ?? ContextSummaryStore.summarize(kind: "resume", source: resume)
            let jobValue = ContextSummaryStore.load(kind: "job", source: job)
                ?? ContextSummaryStore.summarize(kind: "job", source: job)
            guard !Task.isCancelled, self.resumeText == resume, self.jobDescriptionText == job else { return }
            self.resumeSummary = resume.isEmpty ? "" : resumeValue
            self.jobDescriptionSummary = job.isEmpty ? "" : jobValue
            let fullyCached = (resume.isEmpty || ContextSummaryStore.load(kind: "resume", source: resume) != nil)
                && (job.isEmpty || ContextSummaryStore.load(kind: "job", source: job) != nil)
            self.contextSummaryStatus = fullyCached ? "Context summaries ready." : "Local context summary ready."
        }
    }

    // Auxiliary one-shot call used only by the explicit Mermaid correction action,
    // never by the live-copilot turn state machine.
    private func contextSummaryResponse(
        session: AuthSession,
        provider: String,
        model: String,
        usesBYO: Bool,
        messages: [ChatMessage]
    ) async throws -> String {
        let bytes: URLSession.AsyncBytes
        if usesBYO {
            guard let key = rotation.currentKey(provider: provider) else {
                throw BackendError.server("No usable provider credential remains.")
            }
            bytes = try await byoClient.chatStream(
                provider: provider, model: model, apiKey: key.value, imageBase64: nil, messages: messages)
        } else {
            bytes = try await backend.chatStream(
                session: session, provider: provider, model: model, allowPaidSessionExtension: false,
                imagesBase64: [], messages: messages, turnId: UUID().uuidString, operationId: UUID().uuidString)
        }
        var value = ""
        for try await line in bytes.lines {
            if usesBYO {
                if BYOClient.failure(from: line) != nil { throw BackendError.server("The provider request failed.") }
                value += BYOClient.delta(from: line, provider: provider) ?? ""
            } else {
                switch SSEParser.parse(line) {
                case .delta(let delta): value += delta
                case .failure: throw BackendError.server("The provider request failed.")
                default: continue
                }
            }
        }
        let clean = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { throw BackendError.server("The provider returned an empty response.") }
        return clean
    }

    private func handleAuthenticationFailure(_ message: String) async {
        let invalid = session
        chatTask?.cancel()
        heartbeatTask?.cancel()
        heartbeatTask = nil
        await runtime.invalidateAuthentication(auth: invalid)
        SessionStore.clear()
        AccountSnapshotStore.clear()
        session = nil
        account = nil
        screen = .login
        status = message
    }
}
