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
    private var tapInstalled = false
    private var shouldListen = false
    private var cloudTranscript = ""

    var onTranscript: ((String, Bool) -> Void)?
    var onStateChange: ((String) -> Void)?
    private(set) var isListening = false
    var isCloudMode: Bool { cloudTranscriber != nil && !forceNative }

    func configureCloud(transcriber: ((Data) async throws -> String)?, fallbackToNative: Bool) {
        cloudTranscriber = transcriber
        self.fallbackToNative = fallbackToNative
        if transcriber == nil { forceNative = false }
    }

    func start() async {
        guard !isListening, await requestPermissions() else { return }
        shouldListen = true
        cloudTranscript = ""
        cloudAudio.reset()
        if isCloudMode { startCloud() } else { startNative() }
    }

    private func startNative() {
        guard let recognizer, recognizer.isAvailable else { onStateChange?("Speech recognition is unavailable"); return }
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
                if let transcript { self.onTranscript?(transcript, result?.isFinal == true) }
                if let error { self.onStateChange?(error.localizedDescription) }
                if finished { self.finishNative() }
            }
        }
        startEngine(status: forceNative ? "Listening with native fallback…" : "Listening…")
    }

    private func startCloud() {
        stopEngine()
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
            onStateChange?("Ready")
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
                        if !text.isEmpty { self.cloudTranscript += (self.cloudTranscript.isEmpty ? "" : " ") + text }
                    }
                    if !self.cloudTranscript.isEmpty { self.onTranscript?(self.cloudTranscript, item.final) }
                    if item.final { self.onStateChange?("Ready") }
                } catch {
                    self.cloudAudio.reset(); self.onStateChange?("Cloud speech unavailable")
                    if self.fallbackToNative {
                        self.forceNative = true
                        if self.shouldListen { self.startNative() } else { self.onStateChange?("The next recording will use native speech fallback") }
                    }
                    break
                }
            }
            self.cloudDrainTask = nil
        }
    }

    private func finishNative() {
        stopEngine(); recognitionRequest = nil; recognitionTask = nil; isListening = false; shouldListen = false; onStateChange?("Ready")
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
