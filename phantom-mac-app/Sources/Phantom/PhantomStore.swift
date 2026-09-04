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
    @Published var status = "Checking saved session…"
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
        }
    }
    @Published var selectedModelId = "" {
        didSet {
            UserDefaults.standard.set(selectedModelId, forKey: "chat.model")
            if !selectedProviderId.isEmpty {
                UserDefaults.standard.set(selectedModelId, forKey: "chat.model.\(selectedProviderId.lowercased())")
            }
            if attachedScreenshot != nil, !selectedModelSupportsVision { removeScreenshot() }
        }
    }
    @Published var prompt = ""
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
    @Published var attachedScreenshot: Data?
    @Published var isScreenshotPreviewVisible = false
    @Published var isCapturingScreenshot = false
    @Published var isCompact = false
    @Published var isListening = false
    @Published var voiceStatus = "Ready"
    @Published var voiceEnabled: Bool {
        didSet { UserDefaults.standard.set(voiceEnabled, forKey: "voice.enabled") }
    }
    @Published var autoSendAfterVoiceStop: Bool {
        didSet { UserDefaults.standard.set(autoSendAfterVoiceStop, forKey: "voice.autoSend") }
    }
    @Published var opacity: Double {
        didSet {
            UserDefaults.standard.set(opacity, forKey: "window.opacity")
            onWindowPreferencesChanged?(opacity, clickThrough)
        }
    }
    @Published var clickThrough: Bool {
        didSet {
            UserDefaults.standard.set(clickThrough, forKey: "window.clickThrough")
            onWindowPreferencesChanged?(opacity, clickThrough)
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

    var onWindowPreferencesChanged: ((Double, Bool) -> Void)?
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
    private var managedProviders: [ManagedProvider] = []
    private var byoProviders: [ManagedProvider] = BYOCatalog.providers.map { provider in
        ManagedProvider(providerId: provider.providerId, label: provider.label, models: BYOCatalogStore.models(provider: provider.providerId) ?? provider.models)
    }
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
    private var activeRequestStartedAt = Date()
    private var firstChunkRecorded = false
    private var settingsSnapshot: SettingsSnapshot?

    private struct SettingsSnapshot {
        let provider: String, model: String, mode: CopilotMode, style: InterviewDeliveryStyle, resume: String, job: String
        let opacity: Double, freeExtension: Bool, paidExtension: Bool
        let autoPause: Bool, inactivity: Int, preferBYO: Bool, voice: Bool, autoVoice: Bool
        let legacyPath: String, debug: Bool, simulation: String
    }
    private var voicePromptPrefix = ""
    private var previousVoiceTranscript = ""
    private var lastVoiceRenderedPrompt = ""
    private var preserveVoiceEdits = false
    private var voiceDispatchTask: Task<Void, Never>?
    private var pendingVoiceTurnId: String?
    private var lastAutoSentVoiceText = ""

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
        configureSpeechInput()
        scheduleContextWarmup()
    }

    var selectedProvider: ManagedProvider? {
        providers.first(where: { $0.providerId == selectedProviderId })
    }

    var protectionStatus: String {
        ProcessInfo.processInfo.operatingSystemVersion.majorVersion < 15
            ? "AppKit window exclusion enabled — verify in the meeting preview"
            : "Legacy exclusion requested — macOS 15+ does not guarantee omission"
    }

    var selectedModelSupportsVision: Bool {
        (!useBYOProvider && isPremiumAccount)
            || selectedProvider?.models.first(where: { $0.modelId == selectedModelId })?.supportsVision == true
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

    var contextPackHasUnsavedChanges: Bool {
        guard let pack = contextPacks.first(where: { $0.packId == selectedContextPackId }) else { return false }
        return pack.name != contextPackName || pack.resumeText != resumeText || pack.jobDescriptionText != jobDescriptionText
    }

    var resumeWordCount: Int { resumeText.split(whereSeparator: { $0.isWhitespace }).count }
    var jobDescriptionWordCount: Int { jobDescriptionText.split(whereSeparator: { $0.isWhitespace }).count }

    func bootstrap() {
        Diagnostics.log("bootstrap:start")
        onWindowPreferencesChanged?(opacity, clickThrough)
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
            resume: resumeText, job: jobDescriptionText, opacity: opacity,
            freeExtension: allowFreeTrialSessionExtension, paidExtension: allowPaidSessionExtension,
            autoPause: autoPauseOnInactivity, inactivity: inactivityMinutes,
            preferBYO: preferBYOCreditsFirst, voice: voiceEnabled, autoVoice: autoSendAfterVoiceStop,
            legacyPath: legacyAppPath, debug: debugModeEnabled, simulation: debugErrorSimulation
        )
        if canViewDiagnostics { refreshDiagnostics() }
        screen = .settings
    }

    func closeSettings() {
        saveSettings()
    }

    func saveSettings() {
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
    }

    func cancelSettings() {
        guard let old = settingsSnapshot else { screen = .chat; return }
        selectedProviderId = old.provider; selectedModelId = old.model; copilotMode = old.mode; interviewDeliveryStyle = old.style
        resumeText = old.resume; jobDescriptionText = old.job; opacity = old.opacity
        allowFreeTrialSessionExtension = old.freeExtension; allowPaidSessionExtension = old.paidExtension
        autoPauseOnInactivity = old.autoPause; inactivityMinutes = old.inactivity
        preferBYOCreditsFirst = old.preferBYO; voiceEnabled = old.voice; autoSendAfterVoiceStop = old.autoVoice
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
        guard attachedScreenshot != nil else { return }
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
            refreshBYOCatalogs(forceProvider: selectedProviderId)
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
        attachedScreenshot = nil
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
        attachedScreenshot = nil
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

    func captureScreenshot() {
        guard selectedModelSupportsVision else {
            status = "Choose a vision-capable model before attaching a screenshot."
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
                attachedScreenshot = try await onCaptureScreenshot()
                status = "Screenshot attached"
            } catch {
                status = error.localizedDescription
            }
        }
    }

    func removeScreenshot() {
        attachedScreenshot = nil
        isScreenshotPreviewVisible = false
        status = "Screenshot removed"
    }

    func toggleVoiceInput() {
        guard voiceEnabled else {
            voiceStatus = "Enable voice input in Settings"
            return
        }

        if speechInput.isListening {
            speechInput.stop()
            isListening = false
            if autoSendAfterVoiceStop { scheduleVoiceDispatch() }
        } else {
            let existing = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
            voicePromptPrefix = existing.isEmpty ? "" : existing + " "
            previousVoiceTranscript = ""
            lastVoiceRenderedPrompt = prompt
            preserveVoiceEdits = false
            pendingVoiceTurnId = UUID().uuidString
            lastAutoSentVoiceText = ""
            voiceDispatchTask?.cancel()
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
        if text.isEmpty, attachedScreenshot != nil { text = "Please analyze this screenshot." }
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
        if attachedScreenshot != nil, !selectedModelSupportsVision {
            removeScreenshot()
            status = "The screenshot was removed because the selected model does not support vision."
        }
        if useBYOProvider && rotation.keys(for: selectedProviderId).isEmpty {
            status = "Add a \(selectedProviderId) API key in Settings."
            return
        }

        prompt = ""
        let imageBase64 = attachedScreenshot?.base64EncodedString()
        let provider = selectedProviderId
        let model = selectedModelId
        let usesBYO = useBYOProvider
        let requestId = pendingVoiceTurnId ?? UUID().uuidString
        pendingVoiceTurnId = nil
        activeRequestId = requestId
        activeTurnId = requestId
        activeRequestStartedAt = Date()
        firstChunkRecorded = false
        isSending = true
        status = copilotMode == .interview ? "Starting interview session…" : "Starting briefing session…"
        Diagnostics.event("session_started", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle)
        Diagnostics.event("request_dispatched", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["provider": provider, "model": model, "stage": "dispatch"])
        mirrorLiveEvent("request_dispatched", turnId: requestId, fields: ["provider": provider, "model": model, "stage": "dispatch"])

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
                guard activation.allowed else { throw BackendError.server("\(activation.title): \(activation.message)") }
                lastInterviewActivityAt = Date()
                startHeartbeat()
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
                        Diagnostics.event("model_call_started", sessionId: self.copilotSessionId, turnId: requestId, operationId: operationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["provider": provider, "model": model, "attempt": "\(attempt)"])
                        self.mirrorLiveEvent("model_call_started", turnId: requestId, operationId: operationId, fields: ["provider": provider, "model": model, "attempt": "\(attempt)"])
                        if usesBYO {
                            do {
                                let selected = try await self.byoResponseWithRotation(
                                    provider: provider,
                                    selectedModel: self.selectedModelId,
                                    imageBase64: imageBase64,
                                    messages: outbound,
                                    onDelta: onDelta,
                                    onRetryCleanup: onRetryCleanup
                                )
                                self.selectedModelId = selected.model
                                return selected.response
                            } catch where self.allowPaidSessionExtension && !self.managedProviders.isEmpty {
                                self.status = "Switching to managed extension…"
                                let selected = try await self.managedResponseWithRetry(
                                    session: session,
                                    provider: self.managedProviders.first?.providerId ?? provider,
                                    model: self.managedProviders.first?.models.first?.modelId ?? model,
                                    allowPaidSessionExtension: true,
                                    imageBase64: imageBase64,
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
                            imageBase64: imageBase64,
                            messages: outbound,
                            turnId: requestId,
                            operationId: operationId,
                            onDelta: onDelta,
                            onRetryCleanup: onRetryCleanup
                        )
                        self.selectedProviderId = selected.provider
                        self.selectedModelId = selected.model
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
                        return makeStream(finalOutbound, UUID().uuidString, 1)
                    },
                    publish: { chunk in self.append(chunk, to: pendingReply.id) },
                    resetPublishedAttempt: { self.clearReply(pendingReply.id) },
                    protocolRejected: { code in
                        Diagnostics.event("control_frame_rejected", level: "Warning", sessionId: self.copilotSessionId, turnId: requestId, operationId: self.activeOperationId, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["error_code": code, "validation_outcome": "rejected"])
                        self.mirrorLiveEvent("control_frame_rejected", turnId: requestId, operationId: self.activeOperationId, fields: ["error_code": code, "validation_outcome": "rejected"])
                    },
                    decisionParsed: { decision, calls in
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
                            "retrieval_status": decision.action == .retrieve ? "pending" : "not_requested"
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
                }
                Diagnostics.event("answer_completed", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success", "answer_basis": conversationManager.lastAnswerResolution.answerBasis, "question_type": conversationManager.lastAnswerResolution.questionType, "model_call": "\(conversationManager.lastModelCallCount)", "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))", "buffered_characters": "\(messages.first(where: { $0.id == pendingReply.id })?.content.count ?? 0)"])
                try await runtime.metering.resume()
                await runtime.track(category: "billing", event: "interview_session_resumed", attributes: ["reason": "response_succeeded"], accessToken: session.accessToken)
                lastInterviewActivityAt = Date()
                await runtime.track(
                    category: "live_copilot",
                    event: "answer_completed",
                    attributes: ["session_id": copilotSessionId, "turn_id": requestId, "mode": copilotMode.rawValue, "delivery_style": interviewDeliveryStyle.rawValue, "provider": provider, "model": selectedModelId, "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))", "buffered_characters": "\(messages.first(where: { $0.id == pendingReply.id })?.content.count ?? 0)", "outcome": "success"],
                    accessToken: session.accessToken
                )
                status = result.decision.action == .clarify ? "Needs clarification" : "Ready"
            } catch is CancellationError {
                status = "Request cancelled."
                Diagnostics.event("turn_cancelled", level: "Information", sessionId: copilotSessionId, turnId: requestId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "cancelled", "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))"])
            } catch {
                let failureFields = [
                    "provider": provider,
                    "model": selectedModelId,
                    "elapsed_ms": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))",
                    "outcome": "error",
                    "error_code": Self.errorCode(error)
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
                let failureMessage = error is PhantomProtocolError
                    ? "The selected AI model returned an invalid response format. Please retry or choose another model."
                    : "The AI provider could not complete this request. Please retry."
                if let reply, let index = messages.firstIndex(where: { $0.id == reply.id }) {
                    messages[index].content = "Error: \(failureMessage)"
                    messages[index].responseTimeMs = max(0, Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))
                    messages[index].createdAtUtc = Date()
                }
                status = failureMessage
            }
            attachedScreenshot = nil
            Diagnostics.event("session_ended", sessionId: copilotSessionId, turnId: requestId, mode: copilotMode, style: interviewDeliveryStyle)
            ConversationStore.save(messages)
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

    private func byoResponseWithRotation(
        provider: String,
        selectedModel: String,
        imageBase64: String?,
        messages: [ChatMessage],
        onDelta: @escaping (String) -> Void,
        onRetryCleanup: @escaping () -> Void
    ) async throws -> (model: String, response: String) {
        let models = selectedProvider?.models.map(\.modelId) ?? [selectedModel]
        guard var key = rotation.currentKey(provider: provider) else { throw BackendError.server("No usable \(provider) API key remains.") }
        var model = rotation.model(provider: provider, models: models, selected: selectedModel)
        var triedModel = false
        var triedKey = false
        var lastError: Error = BackendError.server("Provider request failed.")

        for attempt in 1...5 {
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
                    imageBase64: imageBase64,
                    messages: messages
                )
                Diagnostics.event("provider_headers_received", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model])
                mirrorLiveEvent("provider_headers_received", operationId: activeOperationId, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": provider, "model": model])
                onRetryCleanup()
                var received = false
                var response = ""
                var terminal: BYOStreamTerminal?
                var chunkCount = 0
                for try await line in bytes.lines {
                    try Task.checkCancellation()
                    if BYOClient.failure(from: line) != nil { throw BackendError.server("The provider stream failed.") }
                    if let delta = BYOClient.delta(from: line, provider: provider) {
                        received = true
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
                byoKeyStatus = "Key \(key.index + 1)/\(rotation.keys(for: provider).count) • \(model)"
                return (model, response)
            } catch {
                lastError = error
                switch rotation.classify(error) {
                case .rateLimited:
                    rotation.markRateLimited(provider: provider, index: key.index)
                    if autoSwitchKeysOnError, rotation.availableKeyCount(provider: provider) > 0,
                       let next = rotation.nextKey(provider: provider) {
                        key = next
                    } else if autoSwitchModelsOnError, !triedModel,
                              let next = rotation.nextModel(provider: provider, models: models, selected: model) {
                        model = next; triedModel = true
                        rotation.clearRateLimits(provider: provider)
                        if let first = rotation.nextKey(provider: provider) { key = first }
                    } else { throw error }
                case .authentication:
                    rotation.markInvalid(provider: provider, index: key.index)
                    guard autoSwitchKeysOnError, rotation.availableKeyCount(provider: provider) > 0,
                          let next = rotation.nextKey(provider: provider) else { throw error }
                    key = next
                case .retryable:
                    if attempt == 1 {
                        try await Task.sleep(nanoseconds: 2_000_000_000)
                    } else if autoSwitchModelsOnError, !triedModel,
                              let next = rotation.nextModel(provider: provider, models: models, selected: model) {
                        model = next; triedModel = true
                    } else if autoSwitchKeysOnError, !triedKey, rotation.keys(for: provider).count > 1,
                              let next = rotation.nextKey(provider: provider) {
                        key = next; triedKey = true
                    } else { throw error }
                case .terminal:
                    throw error
                }
                Diagnostics.event("provider_retry_started", level: "Warning", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "retry", "attempt": "\(attempt + 1)", "provider": provider, "model": model, "error_code": Self.errorCode(error)])
                mirrorLiveEvent("provider_retry_started", operationId: activeOperationId, fields: ["outcome": "retry", "attempt": "\(attempt + 1)", "provider": provider, "model": model, "error_code": Self.errorCode(error)])
                status = "Retry \(attempt + 1)/5 • Key #\(key.index + 1) • \(model)"
                try await Task.sleep(nanoseconds: 500_000_000)
            }
        }
        throw lastError
    }

    private func managedResponseWithRetry(
        session: AuthSession,
        provider: String,
        model: String,
        allowPaidSessionExtension: Bool,
        imageBase64: String?,
        messages: [ChatMessage],
        turnId: String,
        operationId: String,
        onDelta: @escaping (String) -> Void,
        onRetryCleanup: @escaping () -> Void
    ) async throws -> (provider: String, model: String, response: String) {
        var candidates = managedProviders.flatMap { candidate in
            candidate.models.map { (candidate.providerId, $0.modelId) }
        }
        candidates.removeAll { $0.0 == provider && $0.1 == model }
        candidates.insert((provider, model), at: 0)
        var authenticated = self.session ?? session
        var refreshed = false
        var lastError: Error = BackendError.server("Managed provider request failed.")
        for attempt in 0..<5 {
            let candidate = candidates[attempt % candidates.count]
            do {
                let modelStartedAt = Date()
                let bytes = try await backend.chatStream(
                    session: authenticated,
                    provider: candidate.0,
                    model: candidate.1,
                    allowPaidSessionExtension: allowPaidSessionExtension,
                    imageBase64: imageBase64,
                    messages: messages,
                    turnId: turnId,
                    operationId: operationId
                )
                Diagnostics.event("provider_headers_received", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": candidate.0, "model": candidate.1])
                mirrorLiveEvent("provider_headers_received", operationId: activeOperationId, fields: ["elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "provider": candidate.0, "model": candidate.1])
                onRetryCleanup()
                var received = false
                var response = ""
                var completed = false
                var chunkCount = 0
                streamLoop: for try await line in bytes.lines {
                    try Task.checkCancellation()
                    switch SSEParser.parse(line) {
                    case .delta(let delta): received = true; chunkCount += 1; response += delta; onDelta(delta)
                    case .failure: throw BackendError.server("The managed provider stream failed.")
                    case .done: completed = true; break streamLoop
                    case nil: continue
                    }
                }
                guard received else { throw BackendError.server("The AI provider returned an empty response.") }
                guard completed else { throw BackendError.server("The managed provider stream ended unexpectedly.") }
                Diagnostics.event("model_call_completed", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                mirrorLiveEvent("model_call_completed", operationId: activeOperationId, fields: ["outcome": "success", "elapsed_ms": "\(Int(Date().timeIntervalSince(modelStartedAt) * 1_000))", "chunk_count": "\(chunkCount)", "buffered_characters": "\(response.count)"])
                return (candidate.0, candidate.1, response)
            } catch BackendError.http(401, _) where !refreshed {
                authenticated = try await backend.refresh(authenticated, device: device)
                try SessionStore.save(authenticated)
                self.session = authenticated
                refreshed = true
            } catch {
                lastError = error
                guard rotation.classify(error) == .retryable || rotation.classify(error) == .rateLimited else { throw error }
                Diagnostics.event("provider_retry_started", level: "Warning", sessionId: copilotSessionId, turnId: activeTurnId, operationId: activeOperationId, mode: copilotMode, style: interviewDeliveryStyle, fields: ["outcome": "retry", "attempt": "\(attempt + 2)", "provider": candidate.0, "model": candidate.1, "error_code": Self.errorCode(error)])
                mirrorLiveEvent("provider_retry_started", operationId: activeOperationId, fields: ["outcome": "retry", "attempt": "\(attempt + 2)", "provider": candidate.0, "model": candidate.1, "error_code": Self.errorCode(error)])
                status = "Managed retry \(min(attempt + 2, 5))/5 • \(candidate.1)"
                try await Task.sleep(nanoseconds: UInt64(min(attempt + 1, 3)) * 500_000_000)
            }
        }
        throw lastError
    }

    private func append(_ delta: String, to replyId: UUID) {
        guard let index = messages.firstIndex(where: { $0.id == replyId }) else { return }
        messages[index].content += delta
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
        attachedScreenshot = nil
        ConversationStore.clear()
        screen = .login
        status = "Signed out."
        onLogout?()
        guard let current else { return }
        Task {
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
        await finishInterview(auth: session, account: account)
        if !preserveConversationOnTermination { ConversationStore.clear() }
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
        providers = managedProviders
        selectAvailableModel()
        syncRuntimeLane()
        status = providers.isEmpty ? "No managed AI models are currently available." : launchContext.message
        screen = .chat
        startSessionStatusTimer()
        refreshBYOCatalogs()
        if !UserDefaults.standard.bool(forKey: "conversation.restoreAfterRestart") {
            messages.removeAll()
            ConversationStore.clear()
        }
        UserDefaults.standard.removeObject(forKey: "conversation.restoreAfterRestart")
        if isPremiumAccount {
            await loadContextPacks()
        }
    }

    private func selectAvailableModel() {
        if useBYOProvider,
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
        if !provider.models.contains(where: { $0.modelId == selectedModelId }) {
            selectedModelId = provider.models.first?.modelId ?? ""
        }
        loadBYOKey()
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
            self.voiceDispatchTask?.cancel()
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
            Diagnostics.event(
                isFinal ? "transcript_finalized" : "transcript_partial_received",
                level: isFinal ? "Information" : "Debug",
                sessionId: self.copilotSessionId,
                turnId: self.pendingVoiceTurnId ?? UUID().uuidString,
                mode: self.copilotMode,
                style: self.interviewDeliveryStyle,
                fields: ["transcript_length_bucket": Self.lengthBucket(merged.text.count)]
            )
            if isFinal, self.autoSendAfterVoiceStop { self.scheduleVoiceDispatch() }
        }
        speechInput.onStateChange = { [weak self] state in
            guard let self else { return }
            self.voiceStatus = state
            self.isListening = self.speechInput.isListening
        }
    }

    private func scheduleVoiceDispatch() {
        voiceDispatchTask?.cancel()
        let expected = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !expected.isEmpty else { return }
        voiceDispatchTask = Task {
            try? await Task.sleep(nanoseconds: 350_000_000)
            guard !Task.isCancelled,
                  self.prompt.trimmingCharacters(in: .whitespacesAndNewlines) == expected else { return }
            guard expected != self.lastAutoSentVoiceText else {
                Diagnostics.event("request_dispatched", level: "Debug", sessionId: self.copilotSessionId, turnId: self.pendingVoiceTurnId ?? UUID().uuidString, mode: self.copilotMode, style: self.interviewDeliveryStyle, fields: ["duplicate_suppression_count": "1", "outcome": "suppressed"])
                return
            }
            self.lastAutoSentVoiceText = expected
            self.send()
        }
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

    private func refreshBYOCatalogs(forceProvider: String? = nil) {
        Task {
            for provider in byoProviders {
                guard forceProvider == provider.providerId || BYOCatalogStore.isStale(provider: provider.providerId),
                      let key = rotation.keys(for: provider.providerId).first else { continue }
                do {
                    let models = try await byoClient.models(provider: provider.providerId, apiKey: key)
                    guard !models.isEmpty,
                          let index = byoProviders.firstIndex(where: { $0.providerId == provider.providerId }) else { continue }
                    BYOCatalogStore.save(provider: provider.providerId, models: models)
                    byoProviders[index] = ManagedProvider(providerId: provider.providerId, label: provider.label, models: models)
                    if useBYOProvider { providers = byoProviders; selectAvailableModel() }
                } catch {
                    await runtime.track(category: "ai", event: "byo_catalog_refresh_failed", attributes: ["provider": provider.providerId, "error": String(error.localizedDescription.prefix(300))], accessToken: session?.accessToken)
                }
            }
        }
    }

    private func refreshManagedCatalog() {
        guard let session else { return }
        Task {
            do {
                let catalog = try await backend.catalog(accessToken: session.accessToken)
                try ManagedCatalogStore.save(catalog)
                managedProviders = catalog.providers.filter { !$0.models.isEmpty }
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
            sessionStatusText = "Idle"
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
                imageBase64: nil, messages: messages, turnId: UUID().uuidString, operationId: UUID().uuidString)
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
