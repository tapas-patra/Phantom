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
    @Published var interviewType: String {
        didSet { UserDefaults.standard.set(interviewType, forKey: "context.interviewType") }
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
    var onRestart: (() -> Void)?
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
    private var activeRequestStartedAt = Date()
    private var firstChunkRecorded = false
    private var settingsSnapshot: SettingsSnapshot?

    private struct SettingsSnapshot {
        let provider: String, model: String, interviewType: String, resume: String, job: String
        let opacity: Double, freeExtension: Bool, paidExtension: Bool
        let autoPause: Bool, inactivity: Int, preferBYO: Bool, voice: Bool, autoVoice: Bool
        let legacyPath: String, debug: Bool, simulation: String
    }
    private var voicePromptPrefix = ""
    private var previousVoiceTranscript = ""
    private var lastVoiceRenderedPrompt = ""
    private var preserveVoiceEdits = false

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
        interviewType = defaults.string(forKey: "context.interviewType") ?? InterviewPrompt.types[0]
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

    var resumeWordCount: Int { resumeText.split(whereSeparator: \Character.isWhitespace).count }
    var jobDescriptionWordCount: Int { jobDescriptionText.split(whereSeparator: \Character.isWhitespace).count }

    func bootstrap() {
        Diagnostics.log("bootstrap:start")
        onWindowPreferencesChanged?(opacity, clickThrough)
        if let error = runtime.storageFailure {
            Diagnostics.log("bootstrap:storage_unavailable error=\(error)")
            status = error
            launchContext = AppLaunchContext(state: .backendUnavailable, title: "Read-Only Safe Mode", message: error, canOpenApp: false, canStartInterview: false, canResumeLockedInterview: false)
            return
        }
        if let error = configuration.validationError {
            Diagnostics.log("bootstrap:configuration_invalid error=\(error)")
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
                    Diagnostics.log("bootstrap:session_refresh_failed error=\(error.localizedDescription)")
                    await handleAuthenticationFailure("Your saved session expired. Sign in again.")
                }
            } catch {
                do {
                    guard let cached = try AccountSnapshotStore.load(), cached.userId == saved.userId else { throw error }
                    Diagnostics.log("bootstrap:using_cached_snapshot")
                    try await enterApp(with: saved, cachedSnapshot: cached)
                } catch {
                    Diagnostics.log("bootstrap:failed error=\(error.localizedDescription)")
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
                Diagnostics.log("login:failed error=\(error.localizedDescription)")
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
            provider: selectedProviderId, model: selectedModelId, interviewType: interviewType,
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
        selectedProviderId = old.provider; selectedModelId = old.model; interviewType = old.interviewType
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
            diagnosticsText = "Usage pending: \(queue.pendingUsage) | failed: \(queue.failedUsage) | dead-letter: \(queue.deadLetters.count)\nTelemetry queued: \(queue.queuedTelemetry)\nRAG: \(conversationManager.lastTrace)\n\(deadLetters)\n\n\(Diagnostics.text())"
        }
    }
    func clearDiagnostics() {
        guard canViewDiagnostics else { return }
        Diagnostics.clear()
        diagnosticsText = Diagnostics.text()
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
            if autoSendAfterVoiceStop && !prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                Task {
                    try? await Task.sleep(nanoseconds: 350_000_000)
                    send()
                }
            }
        } else {
            let existing = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
            voicePromptPrefix = existing.isEmpty ? "" : existing + " "
            previousVoiceTranscript = ""
            lastVoiceRenderedPrompt = prompt
            preserveVoiceEdits = false
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
        let requestId = UUID().uuidString
        activeRequestId = requestId
        activeRequestStartedAt = Date()
        firstChunkRecorded = false
        isSending = true
        status = "Starting interview session…"
        Diagnostics.log("request_started id=\(requestId) provider=\(provider) model=\(model) lane=\(usesBYO ? "byo" : "managed")")

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
                await refineContextSummariesIfNeeded(session: session, provider: provider, model: model, usesBYO: usesBYO)
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
                status = "Thinking…"

                if isPremiumAccount, hostedKnowledgeBase?.canUseInInterview != true {
                    Diagnostics.log("rag:kb_refresh:start")
                    do {
                        let refreshedKnowledge = try await backend.knowledgeBase(accessToken: session.accessToken)
                        hostedKnowledgeBase = refreshedKnowledge
                        conversationManager.warm(refreshedKnowledge)
                        Diagnostics.log("rag:kb_refresh:success status=\(refreshedKnowledge.status) usable=\(refreshedKnowledge.canUseInInterview) documents=\(refreshedKnowledge.documentCount) chunks=\(refreshedKnowledge.chunkCount) profile=\(refreshedKnowledge.profileCard != nil) experiences=\(refreshedKnowledge.experienceCards?.count ?? 0) projects=\(refreshedKnowledge.projectCards?.count ?? 0)")
                    } catch {
                        Diagnostics.log("rag:kb_refresh:failed error=\(error.localizedDescription)")
                    }
                } else if isPremiumAccount {
                    Diagnostics.log("rag:kb_refresh:skipped reason=cached_ready")
                }
                var plan = InterviewAnswerPlan.clarification
                if usesBYO {
                    Diagnostics.log("planner:skipped reason=byo_lane")
                } else {
                    do {
                        plan = try await backend.interviewPlan(
                            accessToken: session.accessToken,
                            requestId: requestId,
                            provider: provider,
                            model: model,
                            allowPaidSessionExtension: extensionEnabled,
                            question: text,
                            activeEntityId: conversationManager.activeEntityId,
                            recentMessages: Array(messages.dropLast())
                        )
                        Diagnostics.log("planner:resolved intent=\(plan.intent) entity=\(plan.entityType):\(plan.entityId) retrieve=\(plan.retrieve) mode=\(plan.answerMode) confidence=\(plan.confidence)")
                    } catch {
                        Diagnostics.log("planner:failed error=\(error.localizedDescription)")
                    }
                }
                var snippets: [KnowledgeSnippet] = []
                let knowledgeReady = isPremiumAccount && hostedKnowledgeBase?.canUseInInterview == true
                Diagnostics.log("rag:route intent=\(plan.intent) should_search=\(plan.retrieve) preferred_docs=\(plan.preferredDocumentIds.count) kb_ready=\(knowledgeReady)")
                if plan.retrieve, knowledgeReady {
                    do {
                        snippets = try await backend.knowledgeSnippets(
                            accessToken: session.accessToken,
                            query: plan.retrievalQuery,
                            preferredDocumentIds: plan.preferredDocumentIds
                        )
                        Diagnostics.log("rag:hosted_search:success snippets=\(snippets.count)")
                    } catch {
                        Diagnostics.log("rag:hosted_search:failed error=\(error.localizedDescription)")
                    }
                } else if plan.retrieve {
                    let reason = isPremiumAccount ? (hostedKnowledgeBase?.status ?? "missing_kb") : "not_premium"
                    Diagnostics.log("rag:hosted_search:skipped reason=\(reason)")
                }
                let outbound = conversationManager.requestMessages(
                    question: text,
                    interviewType: interviewType,
                    resume: resumeSummary.isEmpty ? resumeText : resumeSummary,
                    jobDescription: jobDescriptionSummary.isEmpty ? jobDescriptionText : jobDescriptionSummary,
                    conversation: Array(messages.dropLast()),
                    modelId: model,
                    knowledgeEnabled: (isPremiumAccount && hostedKnowledgeBase?.canUseInInterview == true)
                        || !snippets.isEmpty,
                    knowledgeBase: hostedKnowledgeBase,
                    knowledgeSnippets: snippets,
                    plan: plan
                )
                await runtime.track(
                    category: "rag",
                    event: "route",
                    attributes: [
                        "trace": conversationManager.lastTrace,
                        "snippetCount": "\(snippets.count)",
                        "intent": conversationManager.lastAnswerResolution.intent.rawValue,
                        "source": conversationManager.lastAnswerResolution.source.rawValue
                    ],
                    accessToken: session.accessToken
                )
                Diagnostics.log("rag:resolved intent=\(conversationManager.lastAnswerResolution.intent.rawValue) source=\(conversationManager.lastAnswerResolution.source.rawValue) snippets=\(snippets.count)")
                let clarificationOptions = plan.clarificationOptions ?? []
                if conversationManager.lastAnswerResolution.source == .clarification,
                   let clarificationQuestion = plan.clarificationQuestion?.trimmingCharacters(in: .whitespacesAndNewlines),
                   !clarificationQuestion.isEmpty,
                   clarificationOptions.count >= 2 {
                    if let index = messages.firstIndex(where: { $0.id == pendingReply.id }) {
                        messages[index].content = clarificationQuestion
                        messages[index].summary = clarificationQuestion
                        messages[index].estimatedTokens = ConversationManager.estimate(messages[index].content)
                        messages[index].answerSource = AnswerSource.clarification.rawValue
                        messages[index].interviewIntent = conversationManager.lastAnswerResolution.intent.rawValue
                        messages[index].clarificationOptions = clarificationOptions
                    }
                    ConversationStore.save(messages)
                    Diagnostics.log("request_completed id=\(requestId) source=Clarification options=\(clarificationOptions.count)")
                    try await runtime.metering.resume()
                    lastInterviewActivityAt = Date()
                    status = "Choose an answer direction"
                    return
                }
                if usesBYO {
                    do {
                        let rotatedModel = try await byoResponseWithRotation(
                            provider: provider,
                            selectedModel: model,
                            imageBase64: imageBase64,
                            messages: outbound,
                            replyId: pendingReply.id
                        )
                        selectedModelId = rotatedModel
                        try await runtime.metering.trackSource(.proBYO, providerId: provider)
                    } catch where allowPaidSessionExtension && !managedProviders.isEmpty {
                        status = "Switching to managed extension…"
                        try await runtime.metering.trackSource(.premiumDebtExtension, providerId: provider)
                        _ = try await managedResponseWithRetry(
                            session: session,
                            provider: managedProviders.first?.providerId ?? provider,
                            model: managedProviders.first?.models.first?.modelId ?? model,
                            allowPaidSessionExtension: true,
                            imageBase64: imageBase64,
                            messages: outbound,
                            replyId: pendingReply.id
                        )
                    }
                } else {
                    try await runtime.metering.trackSource(
                        isFreeTrialAccount ? .freeTrialManaged : (startingPaidExtension ? .premiumDebtExtension : .premiumManaged),
                        providerId: provider
                    )
                    let selected = try await managedResponseWithRetry(
                        session: session,
                        provider: provider,
                        model: model,
                        allowPaidSessionExtension: isFreeTrialAccount ? allowFreeTrialSessionExtension : allowPaidSessionExtension,
                        imageBase64: imageBase64,
                        messages: outbound,
                        replyId: pendingReply.id
                    )
                    selectedProviderId = selected.provider
                    selectedModelId = selected.model
                }
                if let index = messages.firstIndex(where: { $0.id == pendingReply.id }) {
                    let finalized = conversationManager.finalizeAssistantResponse(messages[index].content)
                    messages[index].content = finalized.content
                    messages[index].summary = finalized.summary
                    messages[index].estimatedTokens = ConversationManager.estimate(finalized.content)
                    messages[index].hasCode = finalized.content.contains("```")
                    messages[index].answerSource = conversationManager.lastAnswerResolution.source.rawValue
                    messages[index].interviewIntent = conversationManager.lastAnswerResolution.intent.rawValue
                }
                Diagnostics.log("request_completed id=\(requestId) source=\(conversationManager.lastAnswerResolution.source.rawValue)")
                try await runtime.metering.resume()
                await runtime.track(category: "billing", event: "interview_session_resumed", attributes: ["reason": "response_succeeded"], accessToken: session.accessToken)
                lastInterviewActivityAt = Date()
                await runtime.track(
                    category: "chat",
                    event: "response_completed",
                    attributes: ["requestId": requestId, "provider": provider, "model": selectedModelId, "latencyMs": "\(Int(Date().timeIntervalSince(activeRequestStartedAt) * 1_000))", "characters": "\(messages.first(where: { $0.id == pendingReply.id })?.content.count ?? 0)"],
                    accessToken: session.accessToken
                )
                status = "Ready"
            } catch is CancellationError {
                status = "Request cancelled."
                Diagnostics.log("request_cancelled id=\(requestId)")
            } catch {
                Diagnostics.log("request_failed id=\(requestId) error=\(error.localizedDescription)")
                if let backendError = error as? BackendError, case .http(401, _) = backendError {
                    await handleAuthenticationFailure("Your session expired. Sign in again.")
                    return
                }
                try? await runtime.metering.pause()
                await runtime.track(category: "billing", event: "interview_session_paused", attributes: ["reason": "response_failed"], accessToken: session.accessToken)
                await runtime.track(
                    category: "chat",
                    event: "response_failed",
                    attributes: ["provider": provider, "error": String(error.localizedDescription.prefix(500))],
                    accessToken: session.accessToken
                )
                if let reply, let index = messages.firstIndex(where: { $0.id == reply.id }),
                   messages[index].content.isEmpty {
                    messages[index].content = "Error: \(error.localizedDescription)"
                }
                status = error.localizedDescription
            }
            attachedScreenshot = nil
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
        replyId: UUID
    ) async throws -> String {
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
                let bytes = try await byoClient.chatStream(
                    provider: provider,
                    model: model,
                    apiKey: key.value,
                    imageBase64: imageBase64,
                    messages: messages
                )
                clearReply(replyId)
                var received = false
                for try await line in bytes.lines {
                    try Task.checkCancellation()
                    if line == "data: [DONE]" { break }
                    if let failure = BYOClient.failure(from: line) { throw BackendError.server(failure) }
                    if let delta = BYOClient.delta(from: line, provider: provider) {
                        received = true
                        append(delta, to: replyId)
                    }
                }
                guard received else { throw BackendError.server("The AI provider returned an empty response.") }
                byoKeyStatus = "Key \(key.index + 1)/\(rotation.keys(for: provider).count) • \(model)"
                return model
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
        replyId: UUID
    ) async throws -> (provider: String, model: String) {
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
                let bytes = try await backend.chatStream(
                    session: authenticated,
                    provider: candidate.0,
                    model: candidate.1,
                    allowPaidSessionExtension: allowPaidSessionExtension,
                    imageBase64: imageBase64,
                    messages: messages
                )
                clearReply(replyId)
                var received = false
                for try await line in bytes.lines {
                    try Task.checkCancellation()
                    switch SSEParser.parse(line) {
                    case .delta(let delta): received = true; append(delta, to: replyId)
                    case .failure(let message): throw BackendError.server(message)
                    case .done: break
                    case nil: continue
                    }
                }
                guard received else { throw BackendError.server("The AI provider returned an empty response.") }
                return candidate
            } catch BackendError.http(401, _) where !refreshed {
                authenticated = try await backend.refresh(authenticated, device: device)
                try SessionStore.save(authenticated)
                self.session = authenticated
                refreshed = true
            } catch {
                lastError = error
                guard rotation.classify(error) == .retryable || rotation.classify(error) == .rateLimited else { throw error }
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
            Diagnostics.log("request_first_chunk id=\(activeRequestId) latencyMs=\(latency)")
            Task { await runtime.track(category: "chat", event: "first_desktop_chunk", attributes: ["requestId": activeRequestId, "latencyMs": "\(latency)"], accessToken: session?.accessToken) }
        }
        status = "Streaming response…"
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
        onRestart?()
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
        speechInput.onTranscript = { [weak self] transcript in
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
        }
        speechInput.onStateChange = { [weak self] state in
            guard let self else { return }
            self.voiceStatus = state
            self.isListening = self.speechInput.isListening
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
                Diagnostics.log("managed_catalog_refresh_failed \(error.localizedDescription)")
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
            self.contextSummaryStatus = fullyCached ? "AI context summaries ready." : "Context fallback ready; AI will refine it on first use."
        }
    }

    private func refineContextSummariesIfNeeded(session: AuthSession, provider: String, model: String, usesBYO: Bool) async {
        let inputs = [("resume", resumeText), ("job", jobDescriptionText)]
        for (kind, source) in inputs where !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            if let cached = ContextSummaryStore.load(kind: kind, source: source) {
                if kind == "resume" { resumeSummary = cached } else { jobDescriptionSummary = cached }
                continue
            }
            contextSummaryStatus = "Refining \(kind == "resume" ? "resume" : "job description") summary…"
            let instruction = kind == "resume"
                ? "Extract this resume in exactly these headings: **Name:**, **Role & Experience:**, **Core Skills:**, **Domain Expertise:**, **Key Strengths:**. Use only supplied facts, prioritize concrete technologies, and stay under 150 words."
                : "Summarize this job description in 2-3 sentences under 100 words, covering key requirements, responsibilities, and qualifications. Use only supplied facts."
            do {
                let refined = try await contextSummaryResponse(
                    session: session,
                    provider: provider,
                    model: model,
                    usesBYO: usesBYO,
                    messages: [ChatMessage(role: "system", content: instruction), ChatMessage(role: "user", content: source)]
                )
                guard (kind == "resume" ? resumeText : jobDescriptionText) == source else { continue }
                try ContextSummaryStore.save(kind: kind, source: source, summary: refined)
                if kind == "resume" { resumeSummary = refined } else { jobDescriptionSummary = refined }
            } catch {
                Diagnostics.log("context_summary_refine_failed kind=\(kind) error=\(error.localizedDescription)")
            }
        }
        contextSummaryStatus = "Context summaries ready."
    }

    private func contextSummaryResponse(
        session: AuthSession,
        provider: String,
        model: String,
        usesBYO: Bool,
        messages: [ChatMessage]
    ) async throws -> String {
        let bytes: URLSession.AsyncBytes
        if usesBYO {
            guard let key = rotation.currentKey(provider: provider) else { throw BackendError.server("No usable BYO key for context summary.") }
            bytes = try await byoClient.chatStream(provider: provider, model: model, apiKey: key.value, imageBase64: nil, messages: messages)
        } else {
            bytes = try await backend.chatStream(session: session, provider: provider, model: model, allowPaidSessionExtension: false, imageBase64: nil, messages: messages)
        }
        var value = ""
        for try await line in bytes.lines {
            try Task.checkCancellation()
            if usesBYO {
                if let failure = BYOClient.failure(from: line) { throw BackendError.server(failure) }
                value += BYOClient.delta(from: line, provider: provider) ?? ""
            } else {
                switch SSEParser.parse(line) {
                case .delta(let delta): value += delta
                case .failure(let message): throw BackendError.server(message)
                default: continue
                }
            }
        }
        let clean = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { throw BackendError.server("The summary provider returned an empty response.") }
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
