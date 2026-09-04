import AppKit
import Carbon
import Darwin
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let store = PhantomStore()
    private let window = ProtectedWindow()
    private var hotKeys: [GlobalHotKey] = []
    private var keyMonitor: Any?
    private var windowObservers: [NSObjectProtocol] = []
    private var isQuitting = false
    private var isPreparingToQuit = false
    private var quitSheetOpen = false
    private var expandedFrame: NSRect?

    func applicationDidFinishLaunching(_ notification: Notification) {
        Diagnostics.install()
        NSApp.setActivationPolicy(.accessory)
        installMainMenu()
        window.contentView = NSHostingView(rootView: PhantomRootView(store: store))
        window.installCursorTracking()
        window.delegate = self
        window.center()
        connectStore()
        hotKeys = [
            GlobalHotKey { [weak self] in self?.toggleWindow() },
            GlobalHotKey(keyCode: UInt32(kVK_ANSI_Equal), modifiers: UInt32(cmdKey | controlKey), id: 2) { [weak self] in
                self?.store.showSettings(); self?.showWindow()
            },
            GlobalHotKey(keyCode: UInt32(kVK_F13), modifiers: 0, id: 3) { [weak self] in self?.toggleWindow() },
            GlobalHotKey(keyCode: UInt32(kVK_F14), modifiers: 0, id: 4) { [weak self] in
                self?.requestQuit()
            }
        ]
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.store.screen == .chat, event.keyCode == UInt16(kVK_Return),
                  !event.modifierFlags.contains(.shift),
                  !event.modifierFlags.contains(.option) else { return event }
            self.store.send()
            return nil
        }
        windowObservers.append(NotificationCenter.default.addObserver(forName: NSWindow.didBecomeKeyNotification, object: nil, queue: .main) { notification in
            // Only request capture exclusion here; changing collectionBehavior can close transient AppKit windows.
            (notification.object as? NSWindow)?.sharingType = .none
        })
        showWindow()
        store.bootstrap()
    }

    private func installMainMenu() {
        let mainMenu = NSMenu()
        let editItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = editMenu
        mainMenu.addItem(editItem)
        NSApp.mainMenu = mainMenu
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        false
    }

    private func connectStore() {
        store.onWindowPreferencesChanged = { [weak self] opacity, clickThrough, useFakeCursor in
            self?.window.apply(opacity: opacity, clickThrough: clickThrough)
            self?.window.configureFakeCursor(enabled: useFakeCursor, clickThrough: clickThrough)
        }
        store.onCaptureScreenshot = { [weak self] in
            guard let self else { throw ScreenshotError.captureFailed }
            guard ScreenshotCapture.requestAccess() else { throw ScreenshotError.permissionDenied }
            let targetScreen = self.window.screen
            self.window.orderOut(nil)
            try await Task.sleep(nanoseconds: 250_000_000)
            do {
                let data = try await ScreenshotCapture.selectArea(screen: targetScreen)
                self.showWindow()
                return data
            } catch {
                self.showWindow()
                throw error
            }
        }
        store.onCompactModeChanged = { [weak self] compact in self?.setCompact(compact) }
        store.onLogout = { [weak self] in self?.showWindow() }
        store.onRestart = { [weak self] in
            guard let self else { return false }
            let process = Process()
            process.executableURL = Bundle.main.executableURL ?? URL(fileURLWithPath: CommandLine.arguments[0])
            process.arguments = ["--restart-parent-pid", "\(ProcessInfo.processInfo.processIdentifier)"]
            do {
                try process.run()
                self.isQuitting = true
                NSApp.terminate(nil)
                return true
            } catch {
                return false
            }
        }
        store.onLegacyHandoff = { [weak self] in
            self?.isQuitting = true
            NSApp.terminate(nil)
        }
        window.apply(opacity: store.opacity, clickThrough: store.clickThrough)
        window.configureFakeCursor(enabled: store.useFakeCursor, clickThrough: store.clickThrough)
    }

    private func toggleWindow() {
        if window.isVisible {
            window.orderOut(nil)
        } else {
            if store.clickThrough { store.clickThrough = false }
            showWindow()
        }
    }

    private func showWindow() {
        window.level = .statusBar
        window.orderFrontRegardless()
        if !store.clickThrough { NSApp.activate(ignoringOtherApps: true) }
    }

    private func setCompact(_ compact: Bool) {
        if compact {
            expandedFrame = window.frame
            window.minSize = NSSize(width: 620, height: 86)
            let frame = window.frame
            window.setFrame(
                NSRect(x: frame.minX, y: frame.maxY - 86, width: max(620, frame.width), height: 86),
                display: true
            )
        } else {
            window.minSize = NSSize(width: 820, height: 560)
            if let expandedFrame {
                window.setFrame(expandedFrame, display: true)
            } else {
                window.setContentSize(NSSize(width: 920, height: 640))
            }
        }
        showWindow()
    }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        requestQuit()
        return false
    }

    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard isQuitting else {
            requestQuit()
            return .terminateCancel
        }
        guard !isPreparingToQuit else { return .terminateLater }
        isPreparingToQuit = true
        Task {
            await store.prepareForTermination()
            NSApp.reply(toApplicationShouldTerminate: true)
        }
        return .terminateLater
    }

    private func requestQuit() {
        guard !isQuitting, !quitSheetOpen else { return }
        quitSheetOpen = true
        showWindow()
        window.makeKeyAndOrderFront(nil)
        let alert = NSAlert()
        alert.messageText = "Quit Phantom?"
        alert.informativeText = "Phantom will close completely. The global hide/show shortcut will stop working until you open the app again."
        alert.alertStyle = .warning
        alert.addButton(withTitle: "Quit Phantom")
        alert.addButton(withTitle: "Cancel")
        alert.window.sharingType = .none
        alert.window.level = NSWindow.Level(rawValue: window.level.rawValue + 1)
        alert.window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        alert.beginSheetModal(for: window) { [weak self] response in
            guard let self else { return }
            self.quitSheetOpen = false
            guard response == .alertFirstButtonReturn else { return }
            self.isQuitting = true
            NSApp.terminate(nil)
        }
    }


    func applicationWillTerminate(_ notification: Notification) {
        window.stopFakeCursor()
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        windowObservers.forEach(NotificationCenter.default.removeObserver)
    }
}

@main
enum PhantomMain {
    static func main() {
        if CommandLine.arguments.contains("--self-check") {
            precondition(GlobalHotKey.handles(registeredID: 1, eventID: 1))
            precondition(!GlobalHotKey.handles(registeredID: 1, eventID: 4))
            precondition(MermaidDiagram.renderCandidates("flowchart TD A[Client] --> B[API] C --> D[Worker]").last == "flowchart TD\nA[Client] --> B[API]\nC --> D[Worker]")
            precondition(ChatMessage(role: "assistant", content: "ok", responseTimeMs: 1_234).responseTimeText == "1.2 s")
            precondition(MermaidDiagram.html("flowchart TD\nA[\"quoted\"] --> B", hasLocalRuntime: true).contains("mermaid.min.js"))
            precondition(PhantomStore.extractCorrectedMermaidSource("```mermaid\nflowchart TD\nA-->B\n```") == "flowchart TD\nA-->B")
            precondition(PhantomStore.mergeTranscript(current: "I work", lastRendered: "I am working", previous: "I am working", next: "I am working today", prefix: "", preservingEdits: false).text == "I work today")
            precondition(PhantomStore.mergeTranscript(current: "Question: ", lastRendered: "Question: ", previous: "", next: "Tell me", prefix: "Question: ", preservingEdits: false).text == "Question: Tell me")
            precondition(SSEParser.parse("data: {\"delta\":\"hello\"}") == .delta("hello"))
            precondition(SSEParser.parse("data: {\"error\":\"stopped\"}") == .failure("stopped"))
            precondition(SSEParser.parse("data: [DONE]") == .done)
            precondition(SSEParser.parse("event: ping") == nil)
            precondition(BYOClient.delta(from: "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}", provider: "ChatGPT") == "hi")
            precondition(BYOClient.delta(from: "data: {\"type\":\"content_block_delta\",\"delta\":{\"text\":\"hi\"}}", provider: "Claude") == "hi")
            precondition(BYOClient.delta(from: "data: {\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"hi\"}]}}]}", provider: "Gemini") == "hi")
            precondition(!AccountAccess.hasBYO(tier: "free", credits: 10))
            precondition(AccountAccess.usesBYO(tier: "pro_byo", proCredits: 1, premiumCredits: 0, preferBYO: false))
            precondition(!AccountAccess.usesBYO(tier: "premium", proCredits: 1, premiumCredits: 1, preferBYO: false))
            precondition(AccountAccess.usesBYO(tier: "premium", proCredits: 1, premiumCredits: 1, preferBYO: true))
            precondition(CreditMeteringService.estimateCharge(seconds: 59) == 0)
            precondition(CreditMeteringService.estimateCharge(seconds: 60) == Decimal(string: "0.02"))
            precondition(CreditMeteringService.estimateCharge(seconds: 900) == Decimal(string: "0.25"))
            precondition(CreditMeteringService.estimateCharge(seconds: 3_600) == 1)
            precondition(CreditMeteringService.shouldFinalizeBoundary(tier: "free", elapsed: 900, projectedCharge: 0.25, paidAvailable: 0, existingDebt: 0, allowFreeTrialExtension: false, allowPaidExtension: false))
            precondition(!CreditMeteringService.shouldFinalizeBoundary(tier: "free", elapsed: 900, projectedCharge: 0.25, paidAvailable: 0, existingDebt: 0, allowFreeTrialExtension: true, allowPaidExtension: false))
            precondition(CreditMeteringService.shouldFinalizeBoundary(tier: "premium", elapsed: 7_200, projectedCharge: 2, paidAvailable: 1, existingDebt: 0, allowFreeTrialExtension: false, allowPaidExtension: true))
            precondition(BYOClient.failure(from: "data: {\"error\":{\"message\":\"rate limited\"}}") == "rate limited")
            precondition(BYOClient.terminal(from: "data: [DONE]", provider: "Mistral") == .complete)
            precondition(BYOClient.terminal(from: "data: {\"choices\":[{\"finish_reason\":\"length\"}]}", provider: "Mistral") == .truncated)
            precondition(BYOClient.terminal(from: "data: {\"type\":\"message_stop\"}", provider: "Claude") == .complete)
            precondition(BYOClient.terminal(from: "data: {\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}", provider: "Gemini") == .truncated)
            precondition(ContextSummaryStore.summarize(kind: "job", source: String(repeating: "word ", count: 120)).split(separator: " ").count == 100)
            let emptyKnowledge = StartupSnapshot.KnowledgeBase(knowledgeBaseId: "", name: "", description: "", status: "not_created", embeddingModel: "", embeddingVersion: 0, documentCount: 0, chunkCount: 0, canUseInInterview: false, blockedReason: "", lastProcessedAtUtc: nil, profileCard: nil, experienceCards: [], projectCards: [])
            let restricted = StartupSnapshot(userId: "user", email: "user@example.com", emailVerified: true, accessTier: "premium", wallet: .init(proAvailableCredits: 0, premiumAvailableCredits: 0, premiumNegativeCredits: 0), leaseExpiresAtUtc: Date(timeIntervalSince1970: 0), hasResumableLockedSession: true, lastLockTokenHash: "hash", lastLockedSessionId: "session", offlineModeEnabled: true, canUseDesktopPowerFeatures: false, lastValidatedAtUtc: Date(), hostedKnowledgeBase: emptyKnowledge, source: "self-check")
            let gate = AppLaunchContext.evaluate(restricted, offline: true)
            precondition(gate.canOpenApp && !gate.canStartInterview && gate.canResumeLockedInterview)
            let legacyStartupJSON = #"{"email":"user@example.com","emailVerified":true,"accessTier":"premium","wallet":{"proAvailableCredits":0,"premiumAvailableCredits":1,"premiumNegativeCredits":0}}"#.data(using: .utf8)!
            let legacyStartup = try! JSONDecoder().decode(StartupSnapshot.self, from: legacyStartupJSON)
            precondition(legacyStartup.emailVerified && legacyStartup.hostedKnowledgeBase.status == "not_created")
            precondition(restartParentPID(arguments: ["Phantom", "--restart-parent-pid", "123"]) == 123)
            precondition(restartParentPID(arguments: ["Phantom"]) == nil)
            precondition(InWindowPickerSizing.height(optionCount: 0) == InWindowPickerSizing.minimumHeight)
            precondition(InWindowPickerSizing.height(optionCount: 100) == InWindowPickerSizing.maximumHeight)
            runLiveCopilotFixtures()
            print("Phantom self-check and shared live-copilot fixture suite passed.")
            return
        }

        let restartParent = restartParentPID(arguments: CommandLine.arguments)
        if let restartParent { waitForRestartParent(restartParent) }

        if let bundleIdentifier = Bundle.main.bundleIdentifier,
           let existing = NSRunningApplication.runningApplications(withBundleIdentifier: bundleIdentifier)
            .first(where: {
                $0.processIdentifier != ProcessInfo.processInfo.processIdentifier
                    && $0.processIdentifier != restartParent
            }) {
            existing.activate(options: [.activateAllWindows])
            return
        }

        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.run()
    }

    private static func restartParentPID(arguments: [String]) -> pid_t? {
        guard let flag = arguments.firstIndex(of: "--restart-parent-pid"),
              arguments.indices.contains(flag + 1),
              let value = Int32(arguments[flag + 1]), value > 0 else { return nil }
        return value
    }

    private static func waitForRestartParent(_ parentPID: pid_t) {
        let deadline = Date().addingTimeInterval(30)
        while kill(parentPID, 0) == 0 && Date() < deadline {
            usleep(100_000)
        }
    }

    private static func runLiveCopilotFixtures() {
        struct ParserFixture: Decodable {
            let name: String
            let allowedEntityIds: [String]?
            let allowedDocumentIds: [String]?
            let chunks: [String]
            let expectedAction: String
            let expectedBody: String
            let maxNormalCalls: Int
        }
        struct InvalidFixture: Decodable { let name: String; let frame: String }
        struct AnswerCompletionFixture: Decodable { let name: String; let body: String; let expectedComplete: Bool }
        struct ContractFixture: Decodable { let id: Int; let mode: String; let maxNormalCalls: Int }
        struct LoggingFixture: Decodable { let name: String; let terminalEvents: Int }
        struct FailureFixture: Decodable { let name: String; let statusCode: Int?; let message: String; let expectedKind: String; let cooldownSeconds: Int }
        struct RetryFixture: Decodable { let name: String; let lane: String; let failureKind: String; let attempt: Int; let hasOutput: Bool; let expected: Bool }
        struct LaneFixture: Decodable { let name: String; let from: String; let to: String; let optedIn: Bool; let hasOutput: Bool; let expected: Bool }
        struct ResilienceFixture: Decodable {
            let managedBackendMaxAttempts: Int
            let managedDesktopMaxAttempts: Int
            let byoDesktopMaxAttempts: Int
            let classification: [FailureFixture]
            let retry: [RetryFixture]
            let laneTransitions: [LaneFixture]
        }
        struct Root: Decodable {
            struct DeliveryStyleRequirements: Decodable {
                let standard: [String]
                let desi: [String]
            }
            let version: String
            let parser: [ParserFixture]
            let invalid: [InvalidFixture]
            let answerCompletion: [AnswerCompletionFixture]
            let contracts: [ContractFixture]
            let logging: [LoggingFixture]
            let resilience: ResilienceFixture
            let promptRequirements: [String]
            let deliveryStyleRequirements: DeliveryStyleRequirements
            let repairPromptSuffix: String
            let sensitiveSamples: [String]
        }

        let data = try! Data(contentsOf: fixtureURL())
        let fixtures = try! JSONDecoder().decode(Root.self, from: data)
        precondition(fixtures.version == "live-copilot-v1")
        precondition(fixtures.contracts.count >= 30)
        precondition(Set(fixtures.contracts.map(\.id)).count == fixtures.contracts.count)
        precondition(Set(fixtures.contracts.filter { $0.mode == "interview" }.map(\.id)).isSuperset(of: Set(1...26)))
        precondition(fixtures.contracts.allSatisfy { (1...2).contains($0.maxNormalCalls) })
        precondition(fixtures.logging.count >= 4 && fixtures.logging.allSatisfy { $0.terminalEvents == 1 })
        precondition(fixtures.resilience.managedBackendMaxAttempts == 2)
        precondition(fixtures.resilience.managedDesktopMaxAttempts == ProviderResiliencePolicy.managedDesktopMaxAttempts)
        precondition(fixtures.resilience.byoDesktopMaxAttempts == ProviderResiliencePolicy.byoDesktopMaxAttempts)
        for fixture in fixtures.resilience.classification {
            let decision = ProviderResiliencePolicy.classify(statusCode: fixture.statusCode, message: fixture.message)
            precondition(decision.kind.rawValue == fixture.expectedKind, fixture.name)
            precondition(Int(decision.cooldown) == fixture.cooldownSeconds, fixture.name)
        }
        for fixture in fixtures.resilience.retry {
            let decision = resilienceDecision(fixture.failureKind)
            let maxAttempts = fixture.lane == "managed_desktop"
                ? ProviderResiliencePolicy.managedDesktopMaxAttempts
                : (fixture.lane == "managed_backend" ? fixtures.resilience.managedBackendMaxAttempts : ProviderResiliencePolicy.byoDesktopMaxAttempts)
            let actual = ProviderResiliencePolicy.canRetry(decision, attempt: fixture.attempt, maxAttempts: maxAttempts, hasOutput: fixture.hasOutput)
            precondition(actual == fixture.expected, fixture.name)
        }
        for fixture in fixtures.resilience.laneTransitions {
            precondition(ProviderResiliencePolicy.canCrossLane(from: fixture.from, to: fixture.to, explicitlyOptedIn: fixture.optedIn, hasOutput: fixture.hasOutput) == fixture.expected, fixture.name)
        }
        let firstCallPrompt = CopilotPrompt.firstCall(
            mode: .interview, style: .standard, knowledge: nil, resume: "",
            roleOrMeetingContext: "", activeEvidence: []
        )
        precondition(fixtures.promptRequirements.allSatisfy { firstCallPrompt.contains($0) })
        precondition(fixtures.deliveryStyleRequirements.standard.allSatisfy { firstCallPrompt.contains($0) })
        let desiPrompt = CopilotPrompt.firstCall(
            mode: .interview, style: .desi, knowledge: nil, resume: "",
            roleOrMeetingContext: "", activeEvidence: []
        )
        precondition(fixtures.deliveryStyleRequirements.desi.allSatisfy { desiPrompt.contains($0) })
        precondition(firstCallPrompt != desiPrompt)
        precondition(!firstCallPrompt.contains("Delivery style is Desi"))
        precondition(!desiPrompt.contains("Delivery style is Standard"))
        precondition(ChatDisplayFormatter.blocks("Short answer.").count == 1)
        precondition(ChatDisplayFormatter.blocks("First paragraph.\n\nSecond paragraph.").count == 2)
        let denseAnswer = Array(repeating: "This is a complete sentence that explains one focused part of the answer.", count: 6).joined(separator: " ")
        precondition(ChatDisplayFormatter.blocks(denseAnswer).count == 3)
        let repairPrompt = CopilotPrompt.firstCall(
            mode: .interview, style: .standard, knowledge: nil, resume: "",
            roleOrMeetingContext: "", activeEvidence: [], protocolRepair: true
        )
        precondition(repairPrompt.hasSuffix(fixtures.repairPromptSuffix))
        for fixture in fixtures.parser {
            let parser = PhantomControlFrameParser(
                allowedEntityIds: fixture.allowedEntityIds ?? [],
                allowedDocumentIds: fixture.allowedDocumentIds ?? []
            )
            var body = ""
            for chunk in fixture.chunks { body += try! parser.feed(chunk) }
            let decision = try! parser.complete()
            precondition(decision.action.rawValue == fixture.expectedAction, fixture.name)
            precondition(body == fixture.expectedBody, fixture.name)
            precondition(!body.contains(PhantomControlFrameParser.protocolLine), fixture.name)
            precondition(fixture.maxNormalCalls == (decision.action == .retrieve ? 2 : 1), fixture.name)
        }
        for fixture in fixtures.invalid {
            let parser = PhantomControlFrameParser(
                allowedEntityIds: ["payment-migration"], allowedDocumentIds: ["resume-document-id"]
            )
            do {
                _ = try parser.feed(fixture.frame)
                _ = try parser.complete()
                preconditionFailure("\(fixture.name) was accepted")
            } catch is PhantomProtocolError { }
            catch { preconditionFailure("\(fixture.name) failed with the wrong error") }
        }
        for fixture in fixtures.answerCompletion {
            precondition(LiveCopilotOrchestrator.isCompleteAnswer(fixture.body) == fixture.expectedComplete, fixture.name)
        }
        let allowedFields = ["question_length_bucket", "provider", "model", "answer_basis"]
        for sample in fixtures.sensitiveSamples {
            precondition(!allowedFields.contains(where: { $0.contains(sample) }))
        }

        func resilienceDecision(_ kind: String) -> ProviderFailureDecision {
            switch kind {
            case "rate_limited": return ProviderResiliencePolicy.classify(statusCode: 429)
            case "authentication_failed": return ProviderResiliencePolicy.classify(statusCode: 401)
            case "provider_transient": return ProviderResiliencePolicy.classify(statusCode: 503)
            case "cancelled": return ProviderResiliencePolicy.classify(message: "cancelled")
            default: return ProviderResiliencePolicy.classify(statusCode: 400)
            }
        }
    }

    private static func fixtureURL() -> URL {
        for start in [URL(fileURLWithPath: FileManager.default.currentDirectoryPath), Bundle.main.bundleURL] {
            var directory = start
            for _ in 0..<10 {
                let candidate = directory.appendingPathComponent("shared/live-copilot/fixtures.json")
                if FileManager.default.fileExists(atPath: candidate.path) { return candidate }
                directory.deleteLastPathComponent()
            }
        }
        fatalError("shared/live-copilot/fixtures.json was not found")
    }
}
