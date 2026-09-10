import AppKit
import AVFoundation
import Speech

@MainActor
final class SpeechInputService {
    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-IN")) ?? SFSpeechRecognizer(locale: Locale(identifier: "en-US"))
    private let audioEngine = AVAudioEngine()
    private let cloudAudio = SpeechAudioQueue()
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private var cloudTranscriber: ((Data) async throws -> String)?
    private var cloudDrainTask: Task<Void, Never>?
    private var fallbackToNative = true
    private var forceNative = false
    private var preferCloud = false
    private var cloudCooldownUntil: Date?
    private var cloudCooldownStep = 0
    private var immediateCloudProbePending = false
    private var immediateCloudProbeConsumed = false
    private var pendingCloudRecovery = false
    private var tapInstalled = false
    private var shouldListen = false
    private var cloudTranscript = ""
    private var lastNativeTranscript = ""

    private static let cooldownMinutes = [5, 10, 15]

    var onTranscript: ((String, Bool) -> Void)?
    var onStateChange: ((String) -> Void)?
    private(set) var isListening = false
    var isCloudMode: Bool { cloudTranscriber != nil && preferCloud && !forceNative && !isCoolingDown }

    private var isCoolingDown: Bool {
        guard let until = cloudCooldownUntil else { return false }
        return until > Date()
    }

    func configureCloud(transcriber: ((Data) async throws -> String)?, fallbackToNative: Bool, preferCloud: Bool) {
        cloudTranscriber = transcriber
        self.fallbackToNative = fallbackToNative
        self.preferCloud = preferCloud && transcriber != nil
        if transcriber == nil || !preferCloud {
            forceNative = false
            pendingCloudRecovery = false
            immediateCloudProbePending = false
            immediateCloudProbeConsumed = false
            cloudCooldownUntil = nil
            cloudCooldownStep = 0
        }
    }

    func start() async {
        guard !isListening, await requestPermissions() else { return }
        shouldListen = true
        cloudTranscript = ""
        lastNativeTranscript = ""
        cloudAudio.reset()

        if preferCloud, cloudTranscriber != nil {
            if forceNative {
                if immediateCloudProbePending {
                    // Requirement E: first recovery attempt is the next mic press (no cooldown yet).
                    immediateCloudProbePending = false
                    forceNative = false
                    Diagnostics.log("speech_route_probe route=cloud outcome=immediate_retry")
                    startCloud()
                    return
                }
                if !isCoolingDown {
                    // Cooldown finished — keep this utterance on native, then resume cloud afterward.
                    pendingCloudRecovery = true
                    Diagnostics.log("speech_route_probe route=cloud outcome=scheduled_after_utterance")
                }
                startNative()
                return
            }
            startCloud()
            return
        }
        startNative()
    }

    private var currentCooldownMinutes: Int {
        SpeechInputService.cooldownMinutes[min(cloudCooldownStep, SpeechInputService.cooldownMinutes.count - 1)]
    }

    private func escalateCloudCooldown() {
        let minutes = currentCooldownMinutes
        cloudCooldownUntil = Date().addingTimeInterval(TimeInterval(minutes * 60))
        if cloudCooldownStep < SpeechInputService.cooldownMinutes.count - 1 {
            cloudCooldownStep += 1
        }
    }

    private func clearCloudFallback() {
        forceNative = false
        pendingCloudRecovery = false
        immediateCloudProbePending = false
        immediateCloudProbeConsumed = false
        cloudCooldownUntil = nil
        cloudCooldownStep = 0
    }

    private func enterNativeFallback(scheduleImmediateProbe: Bool) {
        forceNative = true
        if scheduleImmediateProbe {
            immediateCloudProbePending = true
            immediateCloudProbeConsumed = true
            cloudCooldownUntil = nil
            Diagnostics.log("speech_route_changed route=native_fallback reason=cloud_unavailable probe=next_mic")
        } else {
            immediateCloudProbePending = false
            escalateCloudCooldown()
            Diagnostics.log("speech_route_changed route=native_fallback reason=cloud_unavailable cooldown_minutes=\(currentCooldownMinutes)")
        }
    }

    private func startNative() {
        guard let recognizer, recognizer.isAvailable else { onStateChange?("Speech recognition is unavailable"); return }
        Diagnostics.log("speech_capture_started route=\(forceNative ? "native_fallback" : "native")")
        stopEngine()
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        recognitionRequest = request
        installTap { buffer in request.append(buffer) }
        recognitionTask = recognizer.recognitionTask(with: request) { [weak self] result, error in
            let transcript = result?.bestTranscription.formattedString
            let finished = result?.isFinal == true || error != nil
            Task { @MainActor [weak self] in
                guard let self else { return }
                if let transcript {
                    self.lastNativeTranscript = transcript
                    self.onTranscript?(transcript, result?.isFinal == true)
                }
                if result?.isFinal == true, let transcript {
                    Diagnostics.log("speech_transcription_succeeded route=\(self.forceNative ? "native_fallback" : "native") transcript_length_bucket=\(Self.lengthBucket(transcript.count))")
                }
                if let error { self.onStateChange?(error.localizedDescription) }
                if finished { self.finishNative() }
            }
        }
        startEngine(status: forceNative ? "Listening with native fallback…" : "Listening…")
    }

    private func startCloud() {
        stopEngine()
        Diagnostics.log("speech_capture_started route=cloud")
        let format = audioEngine.inputNode.outputFormat(forBus: 0)
        guard format.sampleRate > 0 else { onStateChange?("No microphone input is available"); return }
        let queue = cloudAudio
        installTap { [weak self] buffer in
            for chunk in queue.append(buffer: buffer, sampleRate: format.sampleRate) {
                if queue.enqueue(chunk, final: false) { Task { @MainActor [weak self] in self?.drainCloudQueue() } }
            }
        }
        startEngine(status: "Listening with cloud speech…")
    }

    private func installTap(_ handler: @escaping @Sendable (AVAudioPCMBuffer) -> Void) {
        let input = audioEngine.inputNode
        input.installTap(onBus: 0, bufferSize: 4_096, format: input.outputFormat(forBus: 0)) { buffer, _ in handler(buffer) }
        tapInstalled = true
    }

    private func startEngine(status: String) {
        do { audioEngine.prepare(); try audioEngine.start(); isListening = true; onStateChange?(status) }
        catch { stopEngine(); shouldListen = false; onStateChange?(error.localizedDescription) }
    }

    func stop() {
        shouldListen = false
        let wasCloud = isCloudMode
        stopEngine()
        isListening = false
        if wasCloud {
            let finalData = cloudAudio.flush() ?? Data()
            if cloudAudio.enqueue(finalData, final: true) { drainCloudQueue() }
            onStateChange?("Finishing cloud transcription…")
        } else {
            recognitionRequest?.endAudio(); recognitionTask?.finish(); recognitionRequest = nil; recognitionTask = nil
            // If recognition already finished without an isFinal callback, still finalize for auto-send.
            if !lastNativeTranscript.isEmpty {
                onTranscript?(lastNativeTranscript, true)
            }
            onStateChange?(forceNative ? "Native fallback ready" : "Ready")
            applyPendingCloudRecoveryIfNeeded()
        }
    }

    private func drainCloudQueue() {
        guard cloudDrainTask == nil else { return }
        cloudDrainTask = Task { [weak self] in
            guard let self else { return }
            while let item = self.cloudAudio.next() {
                do {
                    if !item.data.isEmpty, let transcriber = self.cloudTranscriber {
                        let text = try await transcriber(item.data)
                        if !text.isEmpty {
                            self.cloudTranscript += (self.cloudTranscript.isEmpty ? "" : " ") + text
                            self.onStateChange?(item.final ? "Cloud speech recognized" : "Cloud speech active")
                        }
                    }
                    if !self.cloudTranscript.isEmpty { self.onTranscript?(self.cloudTranscript, item.final) }
                    if item.final {
                        let outcome = self.cloudTranscript.isEmpty ? "empty" : "success"
                        Diagnostics.log("speech_transcription_completed route=cloud outcome=\(outcome) transcript_length_bucket=\(Self.lengthBucket(self.cloudTranscript.count))")
                        self.onStateChange?(self.cloudTranscript.isEmpty ? "Cloud speech completed — no speech detected" : "Cloud speech recognized")
                        if outcome == "success" {
                            self.clearCloudFallback()
                        }
                    }
                } catch {
                    Diagnostics.log("speech_transcription_failed route=cloud error_code=\(String(describing: type(of: error))) fallback=\(self.fallbackToNative)")
                    self.cloudAudio.reset(); self.onStateChange?("Cloud speech unavailable")
                    if self.fallbackToNative {
                        // First failure → retry cloud on the next mic. Subsequent failures → 5/10/15 cooldown.
                        let firstFailure = !self.immediateCloudProbeConsumed
                        self.enterNativeFallback(scheduleImmediateProbe: firstFailure)
                        if self.shouldListen {
                            self.startNative()
                        } else if !self.cloudTranscript.isEmpty {
                            // Stop already happened — still finalize so auto-send can run.
                            self.onTranscript?(self.cloudTranscript, true)
                            self.onStateChange?("Native fallback ready")
                        } else {
                            self.onStateChange?(firstFailure
                                ? "The next recording will retry cloud speech"
                                : "The next recording will use native speech fallback")
                        }
                    }
                    break
                }
            }
            self.cloudDrainTask = nil
        }
    }

    private func finishNative() {
        stopEngine(); recognitionRequest = nil; recognitionTask = nil; isListening = false; shouldListen = false
        onStateChange?(forceNative ? "Native fallback ready" : "Ready")
        applyPendingCloudRecoveryIfNeeded()
    }

    private func applyPendingCloudRecoveryIfNeeded() {
        guard pendingCloudRecovery else { return }
        clearCloudFallback()
        Diagnostics.log("speech_route_changed route=cloud reason=probe_success_after_utterance")
        onStateChange?("Cloud speech ready")
    }

    private func stopEngine() {
        if audioEngine.isRunning { audioEngine.stop() }
        if tapInstalled { audioEngine.inputNode.removeTap(onBus: 0); tapInstalled = false }
    }

    func requestPermissions() async -> Bool {
        if AVCaptureDevice.authorizationStatus(for: .audio) == .denied { onStateChange?("Microphone access is denied — enable Phantom in System Settings"); openMicrophoneSettings(); return false }
        onStateChange?("Requesting microphone access…")
        guard await requestMicrophoneAccess() else { onStateChange?("Microphone access is restricted by macOS"); return false }
        if SFSpeechRecognizer.authorizationStatus() == .denied { onStateChange?("Speech Recognition is denied — enable Phantom in System Settings"); openSpeechSettings(); return false }
        onStateChange?("Requesting speech recognition access…")
        guard await requestSpeechAccess() == .authorized else { onStateChange?("Speech Recognition is restricted by macOS"); return false }
        onStateChange?("Microphone and Speech Recognition are allowed"); return true
    }

    var permissionSummary: String { "Microphone: \(label(AVCaptureDevice.authorizationStatus(for: .audio))) • Speech Recognition: \(label(SFSpeechRecognizer.authorizationStatus()))" }
    func openMicrophoneSettings() { openPrivacyPane("Privacy_Microphone") }
    func openSpeechSettings() { openPrivacyPane("Privacy_SpeechRecognition") }

    private func requestSpeechAccess() async -> SFSpeechRecognizerAuthorizationStatus {
        let current = SFSpeechRecognizer.authorizationStatus()
        guard current == .notDetermined else { return current }
        return await withCheckedContinuation { continuation in SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) } }
    }

    private func requestMicrophoneAccess() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized: return true
        case .notDetermined: return await withCheckedContinuation { continuation in AVCaptureDevice.requestAccess(for: .audio) { continuation.resume(returning: $0) } }
        default: return false
        }
    }

    private func openPrivacyPane(_ pane: String) {
        for link in ["x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?\(pane)", "x-apple.systempreferences:com.apple.preference.security?\(pane)"] {
            if let url = URL(string: link), NSWorkspace.shared.open(url) { return }
        }
        NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/System Settings.app"))
    }

    private func label(_ status: AVAuthorizationStatus) -> String {
        switch status { case .authorized: return "Allowed"; case .denied: return "Denied"; case .restricted: return "Restricted"; case .notDetermined: return "Not requested"; @unknown default: return "Unknown" }
    }
    private func label(_ status: SFSpeechRecognizerAuthorizationStatus) -> String {
        switch status { case .authorized: return "Allowed"; case .denied: return "Denied"; case .restricted: return "Restricted"; case .notDetermined: return "Not requested"; @unknown default: return "Unknown" }
    }

    private static func lengthBucket(_ length: Int) -> String {
        switch length { case ...0: return "empty"; case 1...40: return "1-40"; case 41...160: return "41-160"; case 161...640: return "161-640"; default: return "641+" }
    }
}

private final class SpeechAudioQueue: @unchecked Sendable {
    struct Item { let data: Data; let final: Bool }
    private let lock = NSLock()
    private var samples: [Int16] = []
    private var items: [Item] = []
    private var draining = false

    func append(buffer: AVAudioPCMBuffer, sampleRate: Double) -> [Data] {
        guard let channel = buffer.floatChannelData?[0] else { return [] }
        lock.lock(); defer { lock.unlock() }
        let ratio = sampleRate / 16_000
        var position = 0.0
        while Int(position) < Int(buffer.frameLength) {
            let value = max(-1, min(1, channel[Int(position)]))
            samples.append(Int16(value < 0 ? value * 32_768 : value * 32_767))
            position += ratio
        }
        var chunks: [Data] = []
        while samples.count >= 64_000 { chunks.append(data(from: Array(samples.prefix(64_000)))); samples.removeFirst(64_000) }
        return chunks
    }

    func flush() -> Data? {
        lock.lock(); defer { lock.unlock() }
        guard samples.count >= 8_000 else { samples.removeAll(); return nil }
        let result = data(from: samples); samples.removeAll(); return result
    }

    func enqueue(_ data: Data, final: Bool) -> Bool {
        lock.lock(); defer { lock.unlock() }
        items.append(Item(data: data, final: final))
        if draining { return false }
        draining = true; return true
    }

    func next() -> Item? {
        lock.lock(); defer { lock.unlock() }
        guard !items.isEmpty else { draining = false; return nil }
        return items.removeFirst()
    }

    func reset() { lock.lock(); samples.removeAll(); items.removeAll(); draining = false; lock.unlock() }
    private func data(from values: [Int16]) -> Data { values.withUnsafeBytes { Data($0) } }
}
