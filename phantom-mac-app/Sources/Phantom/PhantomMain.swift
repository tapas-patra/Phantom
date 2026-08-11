import AppKit
import Carbon
import SwiftUI

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    private let store = PhantomStore()
    private let window = ProtectedWindow()
    private var hotKeys: [GlobalHotKey] = []
    private var keyMonitor: Any?
    private var restartExecutable: URL?
    private var windowObserver: NSObjectProtocol?
    private var isQuitting = false
    private var isPreparingToQuit = false
    private var quitSheetOpen = false
    private var expandedFrame: NSRect?

    func applicationDidFinishLaunching(_ notification: Notification) {
        Diagnostics.install()
        NSApp.setActivationPolicy(.accessory)
        installMainMenu()
        window.contentView = NSHostingView(rootView: PhantomRootView(store: store))
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
        windowObserver = NotificationCenter.default.addObserver(forName: NSWindow.didBecomeKeyNotification, object: nil, queue: .main) { notification in
            guard let visible = notification.object as? NSWindow, NSApp.windows.contains(visible) else { return }
            visible.sharingType = .none
            visible.collectionBehavior.formUnion([.canJoinAllSpaces, .fullScreenAuxiliary])
        }
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
        store.onWindowPreferencesChanged = { [weak self] opacity, clickThrough in
            self?.window.apply(opacity: opacity, clickThrough: clickThrough)
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
            guard let self else { return }
            self.restartExecutable = Bundle.main.executableURL ?? URL(fileURLWithPath: CommandLine.arguments[0])
            self.isQuitting = true
            NSApp.terminate(nil)
        }
        store.onLegacyHandoff = { [weak self] in
            self?.isQuitting = true
            NSApp.terminate(nil)
        }
        window.apply(opacity: store.opacity, clickThrough: store.clickThrough)
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
            if let executable = restartExecutable {
                let process = Process()
                process.executableURL = executable
                try? process.run()
            }
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
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        if let windowObserver { NotificationCenter.default.removeObserver(windowObserver) }
    }
}

@main
enum PhantomMain {
    static func main() {
        if CommandLine.arguments.contains("--self-check") {
            precondition(GlobalHotKey.handles(registeredID: 1, eventID: 1))
            precondition(!GlobalHotKey.handles(registeredID: 1, eventID: 4))
            precondition(MermaidDiagram.renderCandidates("flowchart TD A[Client] --> B[API] C --> D[Worker]").last == "flowchart TD\nA[Client] --> B[API]\nC --> D[Worker]")
            precondition(MermaidDiagram.html("flowchart TD\nA[\"quoted\"] --> B", hasLocalRuntime: true).contains("mermaid.min.js"))
            precondition(PhantomStore.extractCorrectedMermaidSource("```mermaid\nflowchart TD\nA-->B\n```") == "flowchart TD\nA-->B")
            let ragRouter = ConversationManager()
            precondition(ragRouter.shouldRetrieveKnowledge(for: "What did you personally own?"))
            precondition(ragRouter.shouldRetrieveKnowledge(for: "Why should we hire you?"))
            precondition(!ragRouter.shouldRetrieveKnowledge(for: "What is dependency injection?"))
            precondition(!ragRouter.shouldSearchKnowledge(for: "What is my favorite color?", preferredDocumentIds: []))
            precondition(!ragRouter.shouldSearchKnowledge(for: "Tell me about the Atlas project", preferredDocumentIds: []))
            precondition(ragRouter.shouldSearchKnowledge(for: "What is my recent project?", preferredDocumentIds: ["resume-1"]))
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
            precondition(ContextSummaryStore.summarize(kind: "job", source: String(repeating: "word ", count: 120)).split(separator: " ").count == 100)
            let emptyKnowledge = StartupSnapshot.KnowledgeBase(knowledgeBaseId: "", name: "", description: "", status: "not_created", embeddingModel: "", embeddingVersion: 0, documentCount: 0, chunkCount: 0, canUseInInterview: false, blockedReason: "", lastProcessedAtUtc: nil, profileCard: nil, experienceCards: [], projectCards: [])
            let restricted = StartupSnapshot(userId: "user", email: "user@example.com", emailVerified: true, accessTier: "premium", wallet: .init(proAvailableCredits: 0, premiumAvailableCredits: 0, premiumNegativeCredits: 0), leaseExpiresAtUtc: Date(timeIntervalSince1970: 0), hasResumableLockedSession: true, lastLockTokenHash: "hash", lastLockedSessionId: "session", offlineModeEnabled: true, canUseDesktopPowerFeatures: false, lastValidatedAtUtc: Date(), hostedKnowledgeBase: emptyKnowledge, source: "self-check")
            let gate = AppLaunchContext.evaluate(restricted, offline: true)
            precondition(gate.canOpenApp && !gate.canStartInterview && gate.canResumeLockedInterview)
            let legacyStartupJSON = #"{"email":"user@example.com","emailVerified":true,"accessTier":"premium","wallet":{"proAvailableCredits":0,"premiumAvailableCredits":1,"premiumNegativeCredits":0}}"#.data(using: .utf8)!
            let legacyStartup = try! JSONDecoder().decode(StartupSnapshot.self, from: legacyStartupJSON)
            precondition(legacyStartup.emailVerified && legacyStartup.hostedKnowledgeBase.status == "not_created")
            let conversation = (0..<14).map { ChatMessage(role: $0.isMultiple(of: 2) ? "user" : "assistant", content: "message \($0)") }
            let built = ConversationManager().requestMessages(
                question: "next",
                interviewType: InterviewPrompt.types[0],
                resume: "",
                jobDescription: "",
                conversation: conversation,
                modelId: "gpt-4",
                knowledgeEnabled: false,
                knowledgeBase: nil,
                knowledgeSnippets: []
            )
            precondition(built.first?.role == "system" && built.count == 15)
            let parsed = ConversationManager().finalizeAssistantResponse("Answer\nSUMMARY: concise")
            precondition(parsed.content == "Answer" && parsed.summary == "concise")
            let local = ConversationManager().localKnowledgeSnippets(question: "Swift concurrency", resume: "Built Swift concurrency services", jobDescription: "", preferredDocumentIds: [])
            precondition(local.first?.documentId == "local-resume")
            print("Phantom self-check passed.")
            return
        }

        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.run()
    }
}
