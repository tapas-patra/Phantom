import AppKit
import AVFoundation
import Speech

@MainActor
final class SpeechInputService {
    private let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-IN"))
        ?? SFSpeechRecognizer(locale: Locale(identifier: "en-US"))
    private let audioEngine = AVAudioEngine()
    private var recognitionRequest: SFSpeechAudioBufferRecognitionRequest?
    private var recognitionTask: SFSpeechRecognitionTask?
    private var tapInstalled = false

    var onTranscript: ((String) -> Void)?
    var onStateChange: ((String) -> Void)?
    private(set) var isListening = false

    func start() async {
        guard !isListening else { return }
        guard await requestPermissions() else { return }
        guard let recognizer, recognizer.isAvailable else {
            onStateChange?("Speech recognition is unavailable")
            return
        }

        stop()
        let request = SFSpeechAudioBufferRecognitionRequest()
        request.shouldReportPartialResults = true
        recognitionRequest = request

        let input = audioEngine.inputNode
        let format = input.outputFormat(forBus: 0)
        guard format.sampleRate > 0 else {
            onStateChange?("No microphone input is available")
            return
        }
        input.installTap(onBus: 0, bufferSize: 1_024, format: format) { buffer, _ in
            request.append(buffer)
        }
        tapInstalled = true

        recognitionTask = recognizer.recognitionTask(with: request) { [weak self] result, error in
            let transcript = result?.bestTranscription.formattedString
            let finished = result?.isFinal == true || error != nil
            let errorText = error?.localizedDescription
            Task { @MainActor [weak self] in
                guard let self else { return }
                if let transcript { self.onTranscript?(transcript) }
                if let errorText { self.onStateChange?(errorText) }
                if finished { self.stop() }
            }
        }

        do {
            audioEngine.prepare()
            try audioEngine.start()
            isListening = true
            onStateChange?("Listening…")
        } catch {
            stop()
            onStateChange?(error.localizedDescription)
        }
    }

    func requestPermissions() async -> Bool {
        if AVCaptureDevice.authorizationStatus(for: .audio) == .denied {
            onStateChange?("Microphone access is denied — enable Phantom in System Settings")
            openMicrophoneSettings()
            return false
        }
        onStateChange?("Requesting microphone access…")
        guard await requestMicrophoneAccess() else {
            onStateChange?("Microphone access is restricted by macOS")
            return false
        }
        if SFSpeechRecognizer.authorizationStatus() == .denied {
            onStateChange?("Speech Recognition is denied — enable Phantom in System Settings")
            openSpeechSettings()
            return false
        }
        onStateChange?("Requesting speech recognition access…")
        guard await requestSpeechAccess() == .authorized else {
            onStateChange?("Speech Recognition is restricted by macOS")
            return false
        }
        onStateChange?("Microphone and Speech Recognition are allowed")
        return true
    }

    var permissionSummary: String {
        let microphone = AVCaptureDevice.authorizationStatus(for: .audio)
        let speech = SFSpeechRecognizer.authorizationStatus()
        return "Microphone: \(label(microphone)) • Speech Recognition: \(label(speech))"
    }

    func openMicrophoneSettings() {
        openPrivacyPane("Privacy_Microphone")
    }

    func openSpeechSettings() {
        openPrivacyPane("Privacy_SpeechRecognition")
    }

    func stop() {
        if audioEngine.isRunning { audioEngine.stop() }
        if tapInstalled {
            audioEngine.inputNode.removeTap(onBus: 0)
            tapInstalled = false
        }
        recognitionRequest?.endAudio()
        recognitionTask?.finish()
        recognitionRequest = nil
        recognitionTask = nil
        isListening = false
        onStateChange?("Ready")
    }

    private func requestSpeechAccess() async -> SFSpeechRecognizerAuthorizationStatus {
        let current = SFSpeechRecognizer.authorizationStatus()
        guard current == .notDetermined else { return current }
        return await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) }
        }
    }

    private func requestMicrophoneAccess() async -> Bool {
        switch AVCaptureDevice.authorizationStatus(for: .audio) {
        case .authorized:
            return true
        case .notDetermined:
            return await withCheckedContinuation { continuation in
                AVCaptureDevice.requestAccess(for: .audio) { continuation.resume(returning: $0) }
            }
        default:
            return false
        }
    }

    private func openPrivacyPane(_ pane: String) {
        let links = [
            "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?\(pane)",
            "x-apple.systempreferences:com.apple.preference.security?\(pane)"
        ]
        for link in links {
            if let url = URL(string: link), NSWorkspace.shared.open(url) { return }
        }
        NSWorkspace.shared.open(URL(fileURLWithPath: "/System/Applications/System Settings.app"))
    }

    private func label(_ status: AVAuthorizationStatus) -> String {
        switch status {
        case .authorized: return "Allowed"
        case .denied: return "Denied"
        case .restricted: return "Restricted"
        case .notDetermined: return "Not requested"
        @unknown default: return "Unknown"
        }
    }

    private func label(_ status: SFSpeechRecognizerAuthorizationStatus) -> String {
        switch status {
        case .authorized: return "Allowed"
        case .denied: return "Denied"
        case .restricted: return "Restricted"
        case .notDetermined: return "Not requested"
        @unknown default: return "Unknown"
        }
    }
}
