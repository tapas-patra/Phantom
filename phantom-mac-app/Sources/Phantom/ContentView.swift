import AppKit
import SwiftUI

struct PhantomRootView: View {
    @ObservedObject var store: PhantomStore

    var body: some View {
        Group {
            switch store.screen {
            case .login:
                LoginView(store: store)
            case .chat:
                ChatView(store: store)
            case .settings:
                SettingsView(store: store)
            }
        }
        .frame(minWidth: store.isCompact ? 620 : 820, minHeight: store.isCompact ? 58 : 560)
        .background(PhantomColors.obsidian)
        .preferredColorScheme(.dark)
    }
}

private struct LoginView: View {
    @ObservedObject var store: PhantomStore

    var body: some View {
        HStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 22) {
                BrandMark()
                Spacer()
                Text("Private interview guidance, positioned where you need it.")
                    .font(.system(size: 34, weight: .bold, design: .rounded))
                    .foregroundColor(PhantomColors.frost)
                    .fixedSize(horizontal: false, vertical: true)
                Text("The Mac client uses the same hosted account and managed AI backend as Phantom for Windows.")
                    .font(.system(size: 15))
                    .foregroundColor(PhantomColors.muted)
                    .fixedSize(horizontal: false, vertical: true)
                StatusPill(text: store.protectionStatus, color: PhantomColors.amber)
                Spacer()
            }
            .padding(42)
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .leading)
            .background(PhantomColors.graphite)

            VStack(alignment: .leading, spacing: 18) {
                Text("Desktop Access Gate")
                    .font(.system(size: 12, weight: .semibold, design: .monospaced))
                    .foregroundColor(PhantomColors.blue)
                Text("Welcome to Phantom")
                    .font(.system(size: 28, weight: .bold))
                Text("Sign in to validate your account, wallet, and managed model access.")
                    .foregroundColor(PhantomColors.muted)

                FieldLabel("EMAIL")
                TextField("name@company.com", text: $store.email)
                    .textFieldStyle(.roundedBorder)
                    .accessibilityLabel("Email")

                FieldLabel("PASSWORD")
                SecureField("Password", text: $store.password)
                    .textFieldStyle(.roundedBorder)
                    .onSubmit(store.login)
                    .accessibilityLabel("Password")

                if store.isBusy {
                    ProgressView().controlSize(.small)
                }
                Text(store.status)
                    .font(.system(size: 12, design: .monospaced))
                    .foregroundColor(store.status.lowercased().contains("failed") ? .red : PhantomColors.muted)
                    .fixedSize(horizontal: false, vertical: true)

                HStack {
                    Button("Sign In", action: store.login)
                        .buttonStyle(.borderedProminent)
                        .disabled(store.isBusy)
                        .keyboardShortcut(.return, modifiers: [])
                    Button("Register on Website", action: store.register)
                        .buttonStyle(.bordered)
                    if !store.isBusy {
                        Button("Retry", action: store.retryStartup).buttonStyle(.bordered)
                    }
                }
                Spacer()
            }
            .padding(42)
            .frame(width: 420)
            .frame(maxHeight: .infinity, alignment: .topLeading)
        }
    }
}

private struct ChatView: View {
    private enum Confirmation: Equatable {
        case clear
        case newTopic
    }

    @ObservedObject var store: PhantomStore
    @State private var confirmation: Confirmation?
    @State private var promptHeight: CGFloat = 24

    var body: some View {
        ZStack {
            VStack(spacing: 0) {
                chatHeader
                if !store.isCompact {
                    if store.launchContext.state != .ready {
                        HStack {
                            Image(systemName: "exclamationmark.triangle.fill")
                            Text("\(store.launchContext.title): \(store.launchContext.message)")
                            Spacer()
                        }
                        .font(.caption)
                        .foregroundColor(PhantomColors.amber)
                        .padding(.horizontal, 18)
                        .frame(minHeight: 34)
                        .background(PhantomColors.amber.opacity(0.1))
                    }
                    if store.debugModeEnabled {
                        HStack {
                            Image(systemName: "ladybug.fill")
                            Text("BYO DEBUG: \(store.debugErrorSimulation) • requests \(store.debugRequestCount)")
                            Spacer()
                        }.font(.caption.monospaced()).foregroundColor(.red).padding(.horizontal, 18).frame(minHeight: 30).background(Color.red.opacity(0.1))
                    }
                    Divider().background(PhantomColors.stroke)
                    messageList
                    composer
                }
            }
            if let confirmation {
                protectedConfirmation(confirmation)
            }
            if store.isScreenshotPreviewVisible, !store.attachedScreenshots.isEmpty {
                ScreenshotPreview(
                    images: store.attachedScreenshots.prefix(PhantomStore.maxAttachedScreenshots).compactMap { NSImage(data: $0) },
                    onClose: store.closeScreenshotPreview,
                    onAdd: {
                        store.closeScreenshotPreview()
                        store.captureScreenshot()
                    },
                    onRemoveAt: { store.removeScreenshot(at: $0) },
                    onRemoveAll: { store.removeScreenshot() }
                )
            }
        }
        .background(PhantomColors.obsidian.opacity(0.96))
        .ignoresSafeArea(.container, edges: .top)
    }

    private var chatHeader: some View {
        HStack(spacing: 8) {
            BrandMark(compact: true, showsName: false)
            StatusPill(text: store.creditStatus, color: PhantomColors.blue)
            StatusPill(text: store.activeCreditModeLabel, color: PhantomColors.amber)
            StatusPill(text: store.sessionTimerText.isEmpty ? "00:00:00" : store.sessionTimerText, color: PhantomColors.green)

            if !store.isCompact {
                Image(systemName: "circle.lefthalf.filled")
                    .foregroundColor(PhantomColors.muted)
                    .accessibilityHidden(true)
                Slider(value: $store.opacity, in: 0.35...1)
                    .frame(width: 70)
                    .accessibilityLabel("Window opacity")
            }
            Spacer(minLength: 4)

            if store.isCompact {
                Button(action: store.captureScreenshot) {
                    ZStack(alignment: .topTrailing) {
                        Image(systemName: store.attachedScreenshots.isEmpty ? "camera.viewfinder" : "camera.fill")
                        if store.attachedScreenshotCount > 0 {
                            Text("\(store.attachedScreenshotCount)")
                                .font(.system(size: 9, weight: .bold))
                                .padding(.horizontal, 3)
                                .background(PhantomColors.blue)
                                .clipShape(Capsule())
                                .offset(x: 6, y: -6)
                        }
                    }
                }
                .disabled(!store.selectedModelSupportsVision || store.isCapturingScreenshot || store.attachedScreenshotCount >= PhantomStore.maxAttachedScreenshots)
                .accessibilityLabel("Capture screenshot")

                Button(action: store.toggleVoiceInput) {
                    Image(systemName: store.isListening ? "stop.fill" : "mic.fill")
                        .foregroundColor(store.isListening ? .red : PhantomColors.frost)
                }
                .disabled(!store.voiceEnabled)
                .accessibilityLabel(store.isListening ? "Stop voice input" : "Start voice input")
            } else {
                Button(action: { confirmation = .newTopic }) { Image(systemName: "doc.badge.plus") }
                    .accessibilityLabel("Start new topic")
                Button(action: { confirmation = .clear }) { Image(systemName: "eraser.fill") }
                    .accessibilityLabel("Clear chat and context")
                Button(action: store.copyChat) { Image(systemName: "doc.on.doc") }
                    .disabled(!store.messages.contains(where: { !$0.content.isEmpty }))
                    .accessibilityLabel("Copy whole chat with response timings")
            }

            Button(action: store.toggleClickThrough) {
                Image(systemName: "cursorarrow.rays")
            }
            .accessibilityLabel("Click through Phantom")
            Button(action: store.showSettings) { Image(systemName: "gearshape") }
                .accessibilityLabel("Open settings")
            Button(action: store.toggleCompact) {
                Image(systemName: store.isCompact ? "chevron.down" : "chevron.up")
            }
            .accessibilityLabel(store.isCompact ? "Expand Phantom" : "Collapse Phantom to its bar")
        }
        .buttonStyle(.borderless)
        .padding(.leading, 48)
        .padding(.trailing, 14)
        .frame(height: 58)
        .background(PhantomColors.graphite)
    }

    private var messageList: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(spacing: 12) {
                    if store.messages.isEmpty {
                        VStack(spacing: 12) {
                            Image(systemName: "text.bubble")
                                .font(.system(size: 28))
                                .foregroundColor(PhantomColors.blue)
                            Text("Ask an interview question")
                                .font(.headline)
                            Text("Responses stream from the managed Phantom backend.")
                                .font(.subheadline)
                                .foregroundColor(PhantomColors.muted)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.top, 80)
                    }

                    ForEach(store.messages) { message in
                        MessageBubble(message: message, onCorrectMermaid: store.correctMermaidSyntax, onChooseClarification: store.chooseClarification).id(message.id)
                    }
                }
                .frame(maxWidth: .infinity)
                .padding(.vertical, 18)
                .padding(.horizontal, 24)
            }
            .onChange(of: store.messages) { messages in
                if let id = messages.last?.id {
                    proxy.scrollTo(id, anchor: .bottom)
                }
            }
        }
    }

    private var composer: some View {
        VStack(alignment: .leading, spacing: 10) {
            if !store.attachedScreenshots.isEmpty {
                HStack(spacing: 8) {
                    ForEach(Array(store.attachedScreenshots.enumerated()), id: \.offset) { index, data in
                        if let image = NSImage(data: data) {
                            ZStack(alignment: .topTrailing) {
                                Button(action: store.showScreenshotPreview) {
                                    Image(nsImage: image)
                                        .resizable()
                                        .scaledToFill()
                                        .frame(width: 56, height: 38)
                                        .clipShape(RoundedRectangle(cornerRadius: 6))
                                }
                                .buttonStyle(.plain)
                                .accessibilityLabel("Preview screenshot \(index + 1)")
                                Button {
                                    store.removeScreenshot(at: index)
                                } label: {
                                    Image(systemName: "xmark.circle.fill")
                                        .font(.system(size: 12))
                                }
                                .buttonStyle(.borderless)
                                .offset(x: 4, y: -4)
                                .accessibilityLabel("Remove screenshot \(index + 1)")
                            }
                        }
                    }
                    Text("\(store.attachedScreenshotCount)/\(PhantomStore.maxAttachedScreenshots)")
                        .font(.caption)
                        .foregroundColor(PhantomColors.muted)
                    Spacer()
                    Button("Clear", action: { store.removeScreenshot() })
                        .buttonStyle(.borderless)
                        .font(.caption)
                }
                .padding(8)
                .background(PhantomColors.blue.opacity(0.12))
                .clipShape(RoundedRectangle(cornerRadius: 9))
            }

            HStack(alignment: .bottom, spacing: 10) {
                promptEditor
                    .padding(6)
                    .background(PhantomColors.graphite)
                    .clipShape(RoundedRectangle(cornerRadius: 10))
                    .overlay(RoundedRectangle(cornerRadius: 10).stroke(PhantomColors.stroke))
                    .accessibilityLabel("Message")

                if store.useBYOProvider && store.hasBYOEntitlement {
                    InWindowPicker(
                        "Provider",
                        selection: $store.selectedProviderId,
                        options: store.byoProviderChoices.map { ($0.label, $0.providerId) },
                        compact: true
                    )
                    .frame(minWidth: 100, idealWidth: 120, maxWidth: 160)
                    InWindowPicker(
                        "Model",
                        selection: $store.selectedModelId,
                        options: store.byoModelChoices.map { ($0.displayName, $0.modelId) },
                        compact: true,
                        searchable: true
                    )
                    .frame(minWidth: 120, idealWidth: 150, maxWidth: 220)
                }

                Button(action: store.captureScreenshot) {
                    ZStack(alignment: .topTrailing) {
                        Image(systemName: store.attachedScreenshots.isEmpty ? "camera.viewfinder" : "camera.fill")
                        if store.attachedScreenshotCount > 0 {
                            Text("\(store.attachedScreenshotCount)")
                                .font(.system(size: 9, weight: .bold))
                                .padding(.horizontal, 3)
                                .background(PhantomColors.blue)
                                .clipShape(Capsule())
                                .offset(x: 8, y: -8)
                        }
                    }
                }
                .buttonStyle(.bordered)
                .disabled(!store.selectedModelSupportsVision || store.isCapturingScreenshot || store.attachedScreenshotCount >= PhantomStore.maxAttachedScreenshots)
                .accessibilityLabel("Capture screenshot")

                Button(action: store.toggleVoiceInput) {
                    Image(systemName: store.isListening ? "stop.fill" : "mic.fill")
                        .foregroundColor(store.isListening ? .red : PhantomColors.frost)
                }
                .buttonStyle(.bordered)
                .disabled(!store.voiceEnabled)
                .accessibilityLabel(store.isListening ? "Stop voice input" : "Start voice input")

                Button(action: store.regenerateLastResponse) {
                    Image(systemName: "arrow.clockwise")
                }
                .buttonStyle(.bordered)
                .disabled(store.isSending || !store.messages.contains(where: { $0.role == "user" }))
                .accessibilityLabel("Regenerate last response")

                Button(action: store.send) {
                    Label(store.isSending ? "Replace" : "Send", systemImage: store.isSending ? "arrow.triangle.2.circlepath" : "arrow.up")
                }
                .buttonStyle(.borderedProminent)
                .disabled(
                    (store.prompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && store.attachedScreenshots.isEmpty)
                )
                if store.isSending {
                    Button("Cancel", action: store.cancelCurrentRequest).buttonStyle(.bordered)
                }
            }

            if store.isListening || store.voiceStatus != "Ready" || (!store.status.isEmpty && store.status != "Ready" && store.status != "Account validation passed.") {
                Text(store.isListening || store.voiceStatus != "Ready" ? store.voiceStatus : store.status)
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundColor(store.isListening ? PhantomColors.amber : PhantomColors.muted)
            }
        }
        .padding(16)
        .background(PhantomColors.graphite.opacity(0.9))
    }

    private var promptEditor: some View {
        ZStack(alignment: .topLeading) {
            PromptTextView(text: $store.prompt, height: $promptHeight)
                .frame(height: promptHeight)
            if store.prompt.isEmpty {
                Text("Ask an interview question")
                    .font(.system(size: 14))
                    .foregroundColor(PhantomColors.dim)
                    .padding(.top, 3)
                    .padding(.leading, 5)
                    .allowsHitTesting(false)
            }
        }
    }

    @ViewBuilder
    private func protectedConfirmation(_ value: Confirmation) -> some View {
        let isClear = value == .clear
        ProtectedConfirmation(
            title: isClear ? "Clear everything?" : "Start a new topic?",
            message: isClear
                ? "This removes the conversation, resume, job description, and screenshot attachments."
                : "The conversation and job description will be cleared. Your resume stays available.",
            confirmTitle: isClear ? "Clear Chat and Context" : "Start New Topic",
            destructive: isClear,
            onConfirm: {
                confirmation = nil
                if isClear { store.clearChat() } else { store.startNewTopic() }
            },
            onCancel: { confirmation = nil }
        )
    }
}

private struct PromptTextView: NSViewRepresentable {
    @Binding var text: String
    @Binding var height: CGFloat

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let scrollView = NSScrollView()
        let textView = NSTextView()
        textView.delegate = context.coordinator
        textView.font = .systemFont(ofSize: 14)
        textView.drawsBackground = false
        textView.isRichText = false
        textView.isVerticallyResizable = true
        textView.isHorizontallyResizable = false
        textView.textContainerInset = NSSize(width: 3, height: 3)
        textView.textContainer?.widthTracksTextView = true
        textView.autoresizingMask = [.width]
        scrollView.documentView = textView
        scrollView.drawsBackground = false
        scrollView.borderType = .noBorder
        scrollView.hasVerticalScroller = true
        scrollView.autohidesScrollers = true
        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        context.coordinator.parent = self
        guard let textView = scrollView.documentView as? NSTextView else { return }
        if textView.string != text { textView.string = text }
        context.coordinator.resize(textView)
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: PromptTextView

        init(_ parent: PromptTextView) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            parent.text = textView.string
            resize(textView)
        }

        func resize(_ textView: NSTextView) {
            guard let layout = textView.layoutManager, let container = textView.textContainer else { return }
            layout.ensureLayout(for: container)
            let contentHeight = ceil(layout.usedRect(for: container).height + textView.textContainerInset.height * 2)
            let nextHeight = min(96, max(24, contentHeight))
            textView.enclosingScrollView?.hasVerticalScroller = contentHeight > 96
            if abs(parent.height - nextHeight) > 0.5 {
                DispatchQueue.main.async { self.parent.height = nextHeight }
            }
        }
    }
}

private struct SettingsView: View {
    @ObservedObject var store: PhantomStore

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Button(action: store.cancelSettings) {
                    Label("Cancel", systemImage: "xmark")
                }
                .buttonStyle(.borderless)
                Spacer()
                Text("Settings").font(.headline)
                Spacer()
                Button("Save", action: store.saveSettings)
                    .buttonStyle(.borderedProminent)
                    .disabled(store.resumeOverLimit || store.jobDescriptionOverLimit)
            }
            .padding(.horizontal, 20)
            .frame(height: 58)
            .background(PhantomColors.graphite)

            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    SettingsSection(title: "Capture behavior", systemImage: "rectangle.on.rectangle.slash") {
                        Text(store.protectionStatus)
                            .foregroundColor(PhantomColors.amber)
                        Text("All Phantom windows request NSWindow.SharingType.none, but invisibility is not guaranteed. You must verify the actual meeting, recording, and screen-sharing preview before use; if Phantom is visible and you continue, you accept responsibility for that exposure.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                        Button("Screen Recording Settings", action: store.openScreenRecordingSettings)
                    }

                    SettingsSection(title: "Appearance", systemImage: "circle.lefthalf.filled") {
                        HStack {
                            Text("Window opacity")
                            Slider(value: $store.opacity, in: 0.35...1.0)
                            Text("\(Int(store.opacity * 100))%")
                                .font(.system(size: 12, design: .monospaced))
                                .frame(width: 42)
                        }
                        Toggle("Click through to the application behind Phantom", isOn: $store.settingsClickThrough)
                        Text("Click-through activates after Save closes Settings. Press ⌘ ⌃ ` to show Phantom and automatically disable it.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                        Toggle("Use a fake cursor inside Phantom", isOn: $store.useFakeCursor)
                            .disabled(store.settingsClickThrough)
                        if store.useFakeCursor {
                            HStack {
                                Text("Decoy cursor size")
                                Slider(value: $store.fakeCursorScale, in: 0.5...2.0, step: 0.05)
                                    .disabled(store.settingsClickThrough)
                                Text("\(Int(store.fakeCursorScale * 100))%")
                                    .font(.system(size: 12, design: .monospaced))
                                    .frame(width: 48)
                            }
                        }
                        Text("The size control changes only the stationary, capturable decoy; the protected pointer that follows your movement stays at the system size. macOS capture exclusion is not guaranteed—verify the actual sharing preview before every use. Fake cursor is disabled while click-through is active.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                    }

                    SettingsSection(title: "Voice input", systemImage: "mic") {
                        Toggle("Enable microphone transcription", isOn: $store.voiceEnabled)
                        Toggle("Send automatically after stopping", isOn: $store.autoSendAfterVoiceStop)
                        if store.usesManagedSpeech {
                            ReadOnlyRow(label: "Recognizer", value: "Managed speech with native fallback")
                            Text("Managed speech runs on the Premium lane without exposing provider or model details. Native recognition is used automatically if cloud speech fails.")
                                .font(.caption).foregroundColor(PhantomColors.muted)
                        } else if store.useBYOProvider && store.hasBYOEntitlement {
                            InWindowPicker("Recognizer", selection: $store.speechRecognitionMode, options: [("Native", "Native"), ("Cloud", "Cloud")])
                            if store.speechRecognitionMode == "Cloud" {
                                InWindowPicker("Speech provider", selection: $store.selectedSpeechProviderId, options: store.speechProviders.map { ($0.label, $0.providerId) })
                                InWindowPicker("Speech model", selection: $store.selectedSpeechModelId, options: (store.selectedSpeechProvider?.models ?? []).map { ($0.displayName, $0.modelId) }, searchable: true)
                                TextField("Language code", text: $store.speechLanguage).textFieldStyle(.roundedBorder).accessibilityLabel("Speech language code")
                                Toggle("Use chat provider API keys", isOn: $store.useChatKeysForSpeech)
                                if !store.useChatKeysForSpeech {
                                    SecureField("Dedicated speech API key #1", text: $store.speechAPIKey).textFieldStyle(.roundedBorder)
                                    SecureField("Dedicated speech API key #2 (optional)", text: $store.speechSecondAPIKey).textFieldStyle(.roundedBorder)
                                    HStack {
                                        Button("Save Speech Keys", action: store.saveSpeechKeys)
                                        Button("Remove Speech Keys", role: .destructive, action: store.removeSpeechKeys)
                                    }
                                    Text(store.speechKeyStatus).font(.caption).foregroundColor(PhantomColors.muted)
                                }
                                Toggle("Fall back to native recognition on errors or exhausted credit", isOn: $store.autoFallbackToNativeSpeech)
                                Text("Speech model choices come from the Phantom backend catalog. BYO audio and keys stay between this Mac and the selected provider.")
                                    .font(.caption).foregroundColor(PhantomColors.muted)
                            }
                        } else {
                            ReadOnlyRow(label: "Recognizer", value: "Native")
                        }
                        Text(store.voicePermissionStatus)
                            .font(.system(size: 11, design: .monospaced))
                            .foregroundColor(PhantomColors.muted)
                        HStack {
                            Button("Request Permissions", action: store.requestVoicePermissions)
                            Button("Microphone Settings", action: store.openMicrophoneSettings)
                            Button("Speech Settings", action: store.openSpeechSettings)
                        }
                        Text("Grant both permissions. If either was previously denied, macOS will not show the prompt again; use the Settings buttons, then reopen Phantom.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                    }

                    SettingsSection(title: "Managed AI", systemImage: "cpu") {
                        ReadOnlyRow(label: "Runtime", value: store.useBYOProvider ? "Pro BYO" : "Phantom managed")
                        if store.useBYOProvider && store.hasBYOEntitlement {
                            InWindowPicker(
                                "Provider",
                                selection: $store.selectedProviderId,
                                options: store.byoProviderChoices.map { ($0.label, $0.providerId) }
                            )
                            InWindowPicker(
                                "Model",
                                selection: $store.selectedModelId,
                                options: store.byoModelChoices.map { ($0.displayName, $0.modelId) },
                                searchable: true
                            )
                            Button("Refresh models", action: store.refreshBYOModels)
                                .buttonStyle(.bordered)
                        } else if store.useBYOProvider {
                            Text("BYO lane is active, but this account still needs Pro BYO entitlement and provider keys.")
                                .font(.caption)
                                .foregroundColor(PhantomColors.muted)
                        } else {
                            Text("Provider and model are managed by Phantom on this lane.")
                                .font(.caption)
                                .foregroundColor(PhantomColors.muted)
                        }
                        if store.isFreeTrialAccount {
                            Toggle("Allow the second 15-minute free-trial block", isOn: $store.allowFreeTrialSessionExtension)
                        } else {
                            Toggle("Allow paid continuation up to the protected 1-credit debt cap", isOn: $store.allowPaidSessionExtension)
                        }
                        Toggle("Auto-pause an inactive interview", isOn: $store.autoPauseOnInactivity)
                        if store.autoPauseOnInactivity {
                            Stepper("Pause after \(store.inactivityMinutes) minutes", value: $store.inactivityMinutes, in: 10...120)
                        }
                        if store.hasBothPaidLanes {
                            Toggle("Use BYO credits before Premium credits", isOn: $store.preferBYOCreditsFirst)
                        }
                        if store.hasBYOEntitlement {
                            SecureField("\(store.selectedProviderId) API key #1", text: $store.byoAPIKey)
                                .textFieldStyle(.roundedBorder)
                                .accessibilityLabel("Provider API key one")
                            SecureField("\(store.selectedProviderId) API key #2 (optional)", text: $store.byoSecondAPIKey)
                                .textFieldStyle(.roundedBorder)
                                .accessibilityLabel("Provider API key two")
                            Toggle("Switch API keys automatically on errors", isOn: $store.autoSwitchKeysOnError)
                            Toggle("Switch models automatically on errors", isOn: $store.autoSwitchModelsOnError)
                            HStack {
                                Button("Save Keys", action: store.saveBYOKey)
                                Button("Remove Keys", role: .destructive, action: store.removeBYOKey)
                            }
                            Text(store.byoKeyStatus)
                                .font(.caption)
                                .foregroundColor(PhantomColors.muted)
                            Toggle("Enable BYO error simulator", isOn: $store.debugModeEnabled)
                            if store.debugModeEnabled {
                                InWindowPicker(
                                    "Simulated error",
                                    selection: $store.debugErrorSimulation,
                                    options: ["None", "429", "Timeout", "Random", "Alternating keys", "First two fail"].map { ($0, $0) }
                                )
                                Text("Debug simulation is active and intentionally changes provider requests.").font(.caption).foregroundColor(.red)
                            }
                        } else {
                            Text("Provider keys are restricted to accounts with Pro BYO entitlement.")
                                .font(.caption)
                                .foregroundColor(PhantomColors.muted)
                        }
                    }

                    if store.isPremiumAccount {
                        SettingsSection(title: "Premium Knowledge Base", systemImage: "books.vertical") {
                            Text(store.knowledgeBaseStatus)
                                .foregroundColor(store.hostedKnowledgeBase?.canUseInInterview == true ? PhantomColors.green : PhantomColors.amber)
                            Text("Retrieval is used only when the linked Knowledge Base reports that it is ready for interviews.")
                                .font(.caption)
                                .foregroundColor(PhantomColors.muted)
                        }
                    }

                    SettingsSection(title: "Interview context", systemImage: "doc.text") {
                        if store.isPremiumAccount {
                        InWindowPicker(
                            "Saved context pack",
                            selection: Binding(
                                get: { store.selectedContextPackId },
                                set: { store.selectContextPack($0) }
                            ),
                            options: [("Local draft", "")] + store.contextPacks.map { ($0.name, $0.packId) }
                        )
                        TextField("Context pack name", text: $store.contextPackName)
                            .textFieldStyle(.roundedBorder)
                            .accessibilityLabel("Context pack name")
                        HStack {
                            Button("Save to Account", action: store.saveContextPack)
                                .disabled(store.account?.accessTier.lowercased() != "premium")
                            Button("Reset Changes", action: store.resetContextPackEdits)
                                .disabled(!store.contextPackHasUnsavedChanges)
                            Button("Delete", role: .destructive, action: store.deleteContextPack)
                                .disabled(store.selectedContextPackId.isEmpty)
                        }
                        Text(store.contextPackStatus)
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                        if store.contextPackHasUnsavedChanges {
                            Text("Unsaved context-pack changes").font(.caption).foregroundColor(PhantomColors.amber)
                        }
                        }
                        InWindowPicker(
                            "Live Copilot mode",
                            selection: $store.copilotMode,
                            options: [("Interview", .interview), ("Briefing", .briefing)]
                        )
                        if store.copilotMode == .interview {
                            InWindowPicker(
                                "Interview delivery",
                                selection: $store.interviewDeliveryStyle,
                                options: [("Standard", .standard), ("Desi — Natural Indian English", .desi)]
                            )
                        }
                        Text("Resume • \(store.resumeWordCount)/\(PhantomStore.maxResumeWords) words")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundColor(store.resumeOverLimit ? .red : PhantomColors.frost)
                        TextEditor(text: $store.resumeText)
                            .frame(minHeight: 110)
                            .padding(6)
                            .background(PhantomColors.obsidian)
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(store.resumeOverLimit ? Color.red : PhantomColors.stroke))
                            .accessibilityLabel("Resume")
                        if store.resumeOverLimit {
                            Text("Resume exceeds the \(PhantomStore.maxResumeWords)-word limit.")
                                .font(.caption).foregroundColor(.red)
                        }
                        Text("Job description • \(store.jobDescriptionWordCount)/\(PhantomStore.maxJobDescriptionWords) words")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundColor(store.jobDescriptionOverLimit ? .red : PhantomColors.frost)
                        TextEditor(text: $store.jobDescriptionText)
                            .frame(minHeight: 110)
                            .padding(6)
                            .background(PhantomColors.obsidian)
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                            .overlay(RoundedRectangle(cornerRadius: 8).stroke(store.jobDescriptionOverLimit ? Color.red : PhantomColors.stroke))
                            .accessibilityLabel("Job description")
                        if store.jobDescriptionOverLimit {
                            Text("Job description exceeds the \(PhantomStore.maxJobDescriptionWords)-word limit.")
                                .font(.caption).foregroundColor(.red)
                        }
                        Text("These fields are added to the system context for every interview response.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                        Text(store.contextSummaryStatus).font(.caption).foregroundColor(PhantomColors.green)
                    }

                    SettingsSection(title: "Account", systemImage: "person.crop.circle") {
                        ReadOnlyRow(label: "Email", value: store.session?.email ?? "—")
                        ReadOnlyRow(label: "Version", value: AppVersion.current)
                        ReadOnlyRow(label: "Tier", value: store.account?.accessTier.capitalized ?? "—")
                        ReadOnlyRow(
                            label: "Credits",
                            value: NSDecimalNumber(decimal: store.account?.availableCredits ?? 0).stringValue
                        )
                        ReadOnlyRow(label: "Power flag", value: store.account?.canUseDesktopPowerFeatures == true ? "Enabled" : "Disabled")
                        Button("Restart Phantom and restore this conversation", action: store.restartApp)
                        if store.account?.canUseDesktopPowerFeatures == true {
                            HStack {
                                TextField("Legacy .app path", text: $store.legacyAppPath).textFieldStyle(.roundedBorder)
                                Button("Choose…", action: store.chooseLegacyApp)
                                Button("Launch", action: store.launchLegacyApp)
                            }
                        }
                        Button("Sign Out", role: .destructive, action: store.logout)
                    }

                    SettingsSection(title: "Shortcut", systemImage: "keyboard") {
                        ReadOnlyRow(label: "Hide / show Phantom", value: "⌘  ⌃  `")
                        ReadOnlyRow(label: "Alternate hide / show", value: "F13")
                        ReadOnlyRow(label: "Open Settings", value: "⌘  ⌃  =")
                        ReadOnlyRow(label: "Quit Phantom", value: "F14")
                    }

                    SettingsSection(title: "Companion Mode", systemImage: "iphone") {
                        Text("Pair your phone to trigger Capture & Ask remotely. Grant nothing extra on macOS beyond what Phantom already uses. Pair before the session.")
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                        Toggle("Enable Companion Mode", isOn: $store.companionEnabled)
                        HStack {
                            Button("Show pairing code", action: store.startCompanionPairing)
                            Button("Unpair", action: store.unpairCompanion)
                                .disabled(store.companionPairingId.isEmpty)
                        }
                        if !store.companionPairingCode.isEmpty {
                            Text(store.companionPairingCode)
                                .font(.system(size: 28, weight: .semibold, design: .monospaced))
                                .foregroundColor(PhantomColors.amber)
                            if let qr = QrCodeGenerator.image(from: store.companionPairingQrPayload) {
                                Image(nsImage: qr)
                                    .interpolation(.none)
                                    .resizable()
                                    .aspectRatio(contentMode: .fit)
                                    .frame(width: 180, height: 180)
                                    .accessibilityLabel("Companion pairing QR code")
                            }
                            Text(store.companionPairingQrPayload)
                                .font(.system(size: 10, design: .monospaced))
                                .foregroundColor(PhantomColors.muted)
                                .textSelection(.enabled)
                        }
                        Text(store.companionStatusText)
                            .font(.caption)
                            .foregroundColor(PhantomColors.muted)
                    }

                    if store.canViewDiagnostics {
                    SettingsSection(title: "Diagnostics", systemImage: "stethoscope") {
                        HStack {
                            Button("Refresh", action: store.refreshDiagnostics)
                            Button("Clear Log", action: store.clearDiagnostics)
                            Button("Copy") { NSPasteboard.general.clearContents(); NSPasteboard.general.setString(store.diagnosticsText, forType: .string) }
                        }
                        ScrollView {
                            Text(store.diagnosticsText).font(.system(size: 10, design: .monospaced)).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
                        }.frame(minHeight: 140, maxHeight: 240)
                    }
                    }
                }
                .padding(24)
                .frame(maxWidth: 680)
                .frame(maxWidth: .infinity)
            }
        }
    }
}

enum InWindowPickerSizing {
    static let minimumHeight: CGFloat = 36
    static let maximumHeight: CGFloat = 180
    private static let rowHeight: CGFloat = 32

    static func height(optionCount: Int) -> CGFloat {
        min(maximumHeight, max(minimumHeight, CGFloat(optionCount) * rowHeight + 8))
    }
}

private struct InWindowPicker<Value: Hashable>: View {
    let title: String
    @Binding var selection: Value
    let options: [(title: String, value: Value)]
    var compact: Bool = false
    var searchable: Bool = false
    @State private var isExpanded = false
    @State private var searchText = ""

    init(_ title: String, selection: Binding<Value>, options: [(String, Value)], compact: Bool = false, searchable: Bool = false) {
        self.title = title
        _selection = selection
        self.options = options
        self.compact = compact
        self.searchable = searchable
    }

    private var selectedTitle: String {
        options.first(where: { $0.value == selection })?.title ?? "—"
    }

    private var filteredOptions: [(title: String, value: Value)] {
        let needle = searchText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard searchable, !needle.isEmpty else { return options }
        return options.filter {
            $0.title.localizedCaseInsensitiveContains(needle)
                || String(describing: $0.value).localizedCaseInsensitiveContains(needle)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: compact ? 4 : 6) {
            Button {
                isExpanded.toggle()
                if !isExpanded { searchText = "" }
            } label: {
                HStack(spacing: 8) {
                    if !compact {
                        Text(title).foregroundColor(PhantomColors.muted)
                        Spacer()
                    }
                    Text(selectedTitle).foregroundColor(PhantomColors.frost).lineLimit(1)
                    Image(systemName: "chevron.down")
                        .foregroundColor(PhantomColors.muted)
                        .rotationEffect(.degrees(isExpanded ? 180 : 0))
                }
                .padding(.horizontal, compact ? 8 : 10)
                .frame(minHeight: compact ? 28 : 32)
                .frame(maxWidth: .infinity)
                .background(PhantomColors.obsidian)
                .clipShape(RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(PhantomColors.stroke))
            }
            .buttonStyle(.plain)
            .accessibilityLabel(title)
            .accessibilityValue(selectedTitle)

            if isExpanded {
                VStack(spacing: 6) {
                    if searchable {
                        TextField("Search models", text: $searchText)
                            .textFieldStyle(.plain)
                            .padding(.horizontal, 8)
                            .frame(height: 28)
                            .background(PhantomColors.graphite)
                            .clipShape(RoundedRectangle(cornerRadius: 6))
                            .overlay(RoundedRectangle(cornerRadius: 6).stroke(PhantomColors.stroke))
                            .accessibilityLabel("Search models")
                    }
                    ScrollView(.vertical) {
                        LazyVStack(spacing: 2) {
                            if filteredOptions.isEmpty {
                                Text(options.isEmpty ? "No models" : "No matching models")
                                    .foregroundColor(PhantomColors.muted)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                                    .padding(.horizontal, 10)
                                    .frame(minHeight: compact ? 26 : 30)
                            } else {
                                ForEach(Array(filteredOptions.enumerated()), id: \.offset) { _, option in
                                    Button {
                                        selection = option.value
                                        isExpanded = false
                                        searchText = ""
                                    } label: {
                                        HStack {
                                            Text(option.title)
                                                .foregroundColor(PhantomColors.frost)
                                                .lineLimit(1)
                                                .truncationMode(.middle)
                                            Spacer(minLength: 0)
                                            if option.value == selection {
                                                Image(systemName: "checkmark").foregroundColor(PhantomColors.blue)
                                            }
                                        }
                                        .padding(.horizontal, 10)
                                        .frame(minHeight: compact ? 26 : 30)
                                        .frame(maxWidth: .infinity)
                                        .contentShape(Rectangle())
                                    }
                                    .buttonStyle(.plain)
                                    .accessibilityAddTraits(option.value == selection ? .isSelected : [])
                                }
                            }
                        }
                    }
                    .frame(
                        minWidth: compact ? 100 : 160,
                        maxWidth: compact ? 220 : 320,
                        minHeight: InWindowPickerSizing.minimumHeight,
                        maxHeight: InWindowPickerSizing.maximumHeight
                    )
                    .frame(height: InWindowPickerSizing.height(optionCount: max(filteredOptions.count, 1)))
                }
                .padding(4)
                .background(PhantomColors.obsidian)
                .clipShape(RoundedRectangle(cornerRadius: 7))
                .overlay(RoundedRectangle(cornerRadius: 7).stroke(PhantomColors.stroke))
            }
        }
        .onExitCommand { isExpanded = false; searchText = "" }
    }
}

private struct ProtectedConfirmation: View {
    let title: String
    let message: String
    let confirmTitle: String
    let destructive: Bool
    let onConfirm: () -> Void
    let onCancel: () -> Void

    var body: some View {
        ZStack {
            Color.black.opacity(0.72).ignoresSafeArea()
            VStack(alignment: .leading, spacing: 14) {
                Text(title).font(.title3.weight(.semibold))
                Text(message).foregroundColor(PhantomColors.muted)
                HStack {
                    Spacer()
                    Button("Cancel", action: onCancel).keyboardShortcut(.cancelAction)
                    Button(confirmTitle, action: onConfirm)
                        .keyboardShortcut(.defaultAction)
                        .tint(destructive ? .red : PhantomColors.blue)
                        .buttonStyle(.borderedProminent)
                }
            }
            .padding(22)
            .frame(width: 430)
            .background(PhantomColors.graphite)
            .clipShape(RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(PhantomColors.stroke))
        }
        .zIndex(100)
    }
}

private struct ScreenshotPreview: View {
    let images: [NSImage]
    let onClose: () -> Void
    let onAdd: () -> Void
    let onRemoveAt: (Int) -> Void
    let onRemoveAll: () -> Void
    @State private var selectedIndex = 0

    private var safeIndex: Int {
        guard !images.isEmpty else { return 0 }
        return min(max(0, selectedIndex), images.count - 1)
    }

    var body: some View {
        ZStack {
            Color.black.opacity(0.88).ignoresSafeArea()
            VStack(spacing: 14) {
                HStack {
                    Text(images.count > 1 ? "Screenshots (\(images.count)/\(PhantomStore.maxAttachedScreenshots))" : "Screenshot Preview")
                        .font(.headline)
                    Spacer()
                    Button(action: onClose) { Image(systemName: "xmark") }
                        .buttonStyle(.borderless)
                        .accessibilityLabel("Close screenshot preview")
                }
                if !images.isEmpty {
                    Image(nsImage: images[safeIndex])
                        .resizable()
                        .scaledToFit()
                        .frame(maxWidth: 760, maxHeight: 430)
                        .clipShape(RoundedRectangle(cornerRadius: 10))
                }
                if images.count > 1 {
                    HStack(spacing: 8) {
                        ForEach(Array(images.enumerated()), id: \.offset) { index, image in
                            ZStack(alignment: .topTrailing) {
                                Button {
                                    selectedIndex = index
                                } label: {
                                    Image(nsImage: image)
                                        .resizable()
                                        .scaledToFill()
                                        .frame(width: 64, height: 42)
                                        .clipShape(RoundedRectangle(cornerRadius: 6))
                                        .overlay(
                                            RoundedRectangle(cornerRadius: 6)
                                                .stroke(index == safeIndex ? PhantomColors.blue : PhantomColors.stroke, lineWidth: index == safeIndex ? 2 : 1)
                                        )
                                }
                                .buttonStyle(.plain)
                                .accessibilityLabel("Select screenshot \(index + 1)")
                                Button {
                                    onRemoveAt(index)
                                    if selectedIndex >= images.count - 1 {
                                        selectedIndex = max(0, images.count - 2)
                                    }
                                } label: {
                                    Image(systemName: "xmark.circle.fill")
                                        .font(.system(size: 12))
                                }
                                .buttonStyle(.borderless)
                                .offset(x: 4, y: -4)
                                .accessibilityLabel("Remove screenshot \(index + 1)")
                            }
                        }
                        Spacer()
                    }
                }
                HStack {
                    Button("Add another", action: onAdd)
                        .disabled(images.count >= PhantomStore.maxAttachedScreenshots)
                    Button(images.count > 1 ? "Remove selected" : "Remove", role: .destructive) {
                        if images.count > 1 {
                            onRemoveAt(safeIndex)
                            selectedIndex = max(0, safeIndex - 1)
                        } else {
                            onRemoveAll()
                        }
                    }
                    if images.count > 1 {
                        Button("Remove all", role: .destructive, action: onRemoveAll)
                    }
                    Spacer()
                    Button("Done", action: onClose).buttonStyle(.borderedProminent)
                }
            }
            .padding(20)
            .background(PhantomColors.graphite)
            .clipShape(RoundedRectangle(cornerRadius: 14))
            .overlay(RoundedRectangle(cornerRadius: 14).stroke(PhantomColors.stroke))
            .padding(28)
        }
        .zIndex(110)
        .onChange(of: images.count) { count in
            if count == 0 { onClose() }
            else if selectedIndex >= count { selectedIndex = count - 1 }
        }
    }
}

private struct MessageBubble: View {
    let message: ChatMessage
    let onCorrectMermaid: (String) async throws -> String
    let onChooseClarification: (ClarificationOption, UUID) -> Void

    var body: some View {
        HStack {
            if message.role == "assistant" { Spacer(minLength: 40) }
            VStack(alignment: .leading, spacing: 6) {
                Text(message.role == "assistant" ? "PHANTOM" : "YOU")
                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                    .foregroundColor(message.role == "assistant" ? PhantomColors.blue : PhantomColors.green)
                MarkdownMessageText(content: message.content, onCorrectMermaid: onCorrectMermaid)
                    .frame(maxWidth: .infinity, alignment: .leading)
                if message.role == "assistant", let responseTime = message.responseTimeText, !message.content.isEmpty {
                    Label("Response time: \(responseTime)", systemImage: "clock")
                        .font(.system(size: 10, weight: .medium, design: .monospaced))
                        .foregroundColor(PhantomColors.muted)
                        .accessibilityLabel("Response time \(responseTime)")
                }
                if let options = message.clarificationOptions, !options.isEmpty {
                    VStack(alignment: .leading, spacing: 8) {
                        ForEach(options, id: \.question) { option in
                            Button {
                                onChooseClarification(option, message.id)
                            } label: {
                                Text(option.label)
                                    .multilineTextAlignment(.leading)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                            }
                            .buttonStyle(.bordered)
                        }
                    }
                }
            }
            .padding(14)
            .frame(maxWidth: message.role == "assistant" ? 760 : .infinity, alignment: .leading)
            .background(message.role == "assistant" ? PhantomColors.graphite : PhantomColors.blue.opacity(0.16))
            .clipShape(RoundedRectangle(cornerRadius: 12))
            .overlay(RoundedRectangle(cornerRadius: 12).stroke(PhantomColors.stroke))
            if message.role != "assistant" { Spacer(minLength: 40) }
        }
        .frame(maxWidth: .infinity)
    }
}

private struct MarkdownMessageText: View {
    let content: String
    let onCorrectMermaid: (String) async throws -> String
    @State private var correctedDiagram: String?
    @State private var diagramReloadID = UUID()
    @State private var renderFailed = false
    @State private var isCorrecting = false
    @State private var correctionError = ""

    var body: some View {
        if content.isEmpty {
            Text("…").foregroundColor(PhantomColors.frost)
        } else if let mermaid {
            VStack(alignment: .leading, spacing: 10) {
                if !mermaid.explanation.isEmpty {
                    Text((try? AttributedString(markdown: mermaid.explanation)) ?? AttributedString(mermaid.explanation))
                        .textSelection(.enabled)
                        .lineSpacing(4)
                        .foregroundColor(PhantomColors.frost)
                }
                MermaidDiagram(source: correctedDiagram ?? mermaid.diagram, reloadID: diagramReloadID) { renderFailed = $0 }
                    .frame(minHeight: 220, maxHeight: 420)
                if renderFailed {
                    HStack {
                        Button(isCorrecting ? "Correcting…" : "Correct syntax") {
                            isCorrecting = true
                            correctionError = ""
                            Task {
                                do {
                                    correctedDiagram = try await onCorrectMermaid(correctedDiagram ?? mermaid.diagram)
                                    diagramReloadID = UUID()
                                    renderFailed = false
                                } catch {
                                    correctionError = error.localizedDescription
                                    Diagnostics.log("mermaid:correction:failed code=render_failed")
                                }
                                isCorrecting = false
                            }
                        }
                        .disabled(isCorrecting)
                        if !correctionError.isEmpty {
                            Text(correctionError).font(.caption).foregroundColor(PhantomColors.amber)
                        }
                    }
                }
            }
        } else {
            VStack(alignment: .leading, spacing: 12) {
                ForEach(Array(ChatDisplayFormatter.blocks(content).enumerated()), id: \.offset) { _, block in
                    Text((try? AttributedString(
                        markdown: block,
                        options: .init(interpretedSyntax: .full)
                    )) ?? AttributedString(block))
                    .textSelection(.enabled)
                    .lineSpacing(5)
                    .foregroundColor(PhantomColors.frost)
                }
            }
        }
    }

    private var mermaid: (explanation: String, diagram: String)? {
        guard let opening = content.range(of: "```mermaid"),
              let closing = content.range(of: "```", range: opening.upperBound..<content.endIndex) else { return nil }
        let diagram = String(content[opening.upperBound..<closing.lowerBound]).trimmingCharacters(in: .whitespacesAndNewlines)
        let explanation = String(content[..<opening.lowerBound] + content[closing.upperBound...]).trimmingCharacters(in: .whitespacesAndNewlines)
        return diagram.isEmpty ? nil : (explanation, diagram)
    }
}

enum ChatDisplayFormatter {
    static func blocks(_ content: String) -> [String] {
        let normalized = content
            .replacingOccurrences(of: "\r\n", with: "\n")
            .trimmingCharacters(in: .whitespacesAndNewlines)
        guard !normalized.isEmpty else { return [] }
        guard !normalized.contains("```") else { return [normalized] }

        let authoredParagraphs = normalized
            .components(separatedBy: "\n\n")
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        if authoredParagraphs.count > 1 { return authoredParagraphs }

        guard normalized.count >= 360, !normalized.contains("\n") else { return [normalized] }
        var sentences: [String] = []
        normalized.enumerateSubstrings(in: normalized.startIndex..<normalized.endIndex, options: .bySentences) { substring, _, _, _ in
            if let sentence = substring?.trimmingCharacters(in: .whitespacesAndNewlines), !sentence.isEmpty {
                sentences.append(sentence)
            }
        }
        guard sentences.count >= 4 else { return [normalized] }

        return stride(from: 0, to: sentences.count, by: 2).map { start in
            sentences[start..<min(start + 2, sentences.count)].joined(separator: " ")
        }
    }
}

private struct SettingsSection<Content: View>: View {
    let title: String
    let systemImage: String
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Label(title, systemImage: systemImage)
                .font(.headline)
                .foregroundColor(PhantomColors.frost)
            content
        }
        .padding(18)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(PhantomColors.graphite)
        .clipShape(RoundedRectangle(cornerRadius: 14))
        .overlay(RoundedRectangle(cornerRadius: 14).stroke(PhantomColors.stroke))
    }
}

private struct ReadOnlyRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack(alignment: .top) {
            Text(label).foregroundColor(PhantomColors.muted).frame(width: 90, alignment: .leading)
            Text(value).textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
        }
        .font(.system(size: 13))
    }
}

private struct BrandMark: View {
    var compact = false
    var showsName = true

    @ViewBuilder
    private var icon: some View {
        if let url = Bundle.main.url(forResource: "phantom-logo", withExtension: "png"),
           let image = NSImage(contentsOf: url) {
            Image(nsImage: image).resizable().scaledToFit()
        } else {
            Image(systemName: "viewfinder")
                .foregroundColor(PhantomColors.blue)
                .font(.system(size: compact ? 16 : 24, weight: .semibold))
        }
    }

    var body: some View {
        HStack(spacing: 10) {
            ZStack {
                RoundedRectangle(cornerRadius: compact ? 7 : 12)
                    .fill(PhantomColors.blue.opacity(0.18))
                icon
            }
            .frame(width: compact ? 32 : 48, height: compact ? 32 : 48)
            if showsName {
                Text("PHANTOM")
                    .font(.system(size: compact ? 13 : 18, weight: .bold, design: .monospaced))
                    .foregroundColor(PhantomColors.frost)
            }
        }
    }
}

private struct StatusPill: View {
    let text: String
    let color: Color

    var body: some View {
        HStack(spacing: 7) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(text).lineLimit(1)
        }
        .font(.system(size: 10, weight: .medium, design: .monospaced))
        .foregroundColor(PhantomColors.frost)
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(color.opacity(0.12))
        .clipShape(Capsule())
        .overlay(Capsule().stroke(color.opacity(0.35)))
        .fixedSize(horizontal: true, vertical: false)
    }
}

private struct FieldLabel: View {
    let text: String
    init(_ text: String) { self.text = text }

    var body: some View {
        Text(text)
            .font(.system(size: 10, weight: .bold, design: .monospaced))
            .foregroundColor(PhantomColors.muted)
    }
}

private enum PhantomColors {
    static let obsidian = Color(red: 10 / 255, green: 12 / 255, blue: 15 / 255)
    static let graphite = Color(red: 21 / 255, green: 26 / 255, blue: 32 / 255)
    static let frost = Color(red: 230 / 255, green: 237 / 255, blue: 243 / 255)
    static let muted = Color(red: 145 / 255, green: 158 / 255, blue: 171 / 255)
    static let dim = Color(red: 95 / 255, green: 107 / 255, blue: 119 / 255)
    static let blue = Color(red: 73 / 255, green: 185 / 255, blue: 255 / 255)
    static let green = Color(red: 67 / 255, green: 209 / 255, blue: 122 / 255)
    static let amber = Color(red: 255 / 255, green: 181 / 255, blue: 74 / 255)
    static let stroke = Color.white.opacity(0.12)
}
