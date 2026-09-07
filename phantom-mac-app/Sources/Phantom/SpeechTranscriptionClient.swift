import Foundation

@MainActor
final class SpeechTranscriptionClient {
    private let backend: BackendClient
    private let rotation: APIRotationManager

    init(backend: BackendClient, rotation: APIRotationManager) {
        self.backend = backend
        self.rotation = rotation
    }

    static func selfCheck() -> Bool {
        let silent = Data(repeating: 0, count: 16_000)
        let voiced = Data(repeating: 0x10, count: 16_000)
        let wave = wav(silent)
        return !containsSpeech(silent)
            && containsSpeech(voiced)
            && String(data: wave.prefix(4), encoding: .ascii) == "RIFF"
            && wave.count == silent.count + 44
    }

    func transcribe(
        pcm16: Data,
        session: AuthSession,
        managed: Bool,
        provider: String,
        model: String,
        language: String,
        useChatKeys: Bool
    ) async throws -> String {
        guard Self.containsSpeech(pcm16) else { return "" }
        let wav = Self.wav(pcm16)
        if managed { return try await backend.transcribeManagedSpeech(accessToken: session.accessToken, wav: wav, language: language) }
        guard !model.isEmpty else { throw BackendError.server("Choose a speech model in Settings.") }
        let keys = useChatKeys ? rotation.keys(for: provider) : rotation.speechKeys(provider: provider)
        guard !keys.isEmpty else { throw BackendError.server("Add a speech API key or enable shared chat keys.") }

        var lastError: Error?
        for attempt in 0..<min(2, keys.count) {
            let scope = "\(provider):\(useChatKeys ? "chat" : "dedicated")"
            guard let selected = nextKey(provider: scope, keys: keys) else { break }
            do {
                let text = try await Self.post(wav: wav, provider: provider, model: model, language: language, apiKey: selected.value)
                clearCooldown(provider: scope, index: selected.index)
                return text
            } catch {
                lastError = error
                let decision = rotation.classify(error)
                if decision.canRotateCredential { setCooldown(provider: scope, index: selected.index, duration: decision.cooldown) }
                if !ProviderResiliencePolicy.canRetry(decision, attempt: attempt + 1, maxAttempts: 2, hasOutput: false) { break }
            }
        }
        throw lastError ?? BackendError.server("No speech API key is currently available.")
    }

    private func nextKey(provider: String, keys: [String]) -> (index: Int, value: String)? {
        let defaults = UserDefaults.standard
        let indexKey = "speech.rotation.\(provider.lowercased()).lastKey"
        let last = defaults.object(forKey: indexKey) == nil ? -1 : defaults.integer(forKey: indexKey)
        for offset in 1...keys.count {
            let index = (last + offset) % keys.count
            if cooldowns(provider)[String(index), default: 0] > Date().timeIntervalSince1970 { continue }
            defaults.set(index, forKey: indexKey)
            return (index, keys[index])
        }
        return nil
    }

    private func cooldowns(_ provider: String) -> [String: Double] {
        UserDefaults.standard.dictionary(forKey: "speech.rotation.\(provider.lowercased()).cooldowns") as? [String: Double] ?? [:]
    }

    private func setCooldown(provider: String, index: Int, duration: TimeInterval) {
        var values = cooldowns(provider)
        values[String(index)] = Date().addingTimeInterval(duration).timeIntervalSince1970
        UserDefaults.standard.set(values, forKey: "speech.rotation.\(provider.lowercased()).cooldowns")
    }

    private func clearCooldown(provider: String, index: Int) {
        var values = cooldowns(provider)
        values.removeValue(forKey: String(index))
        UserDefaults.standard.set(values, forKey: "speech.rotation.\(provider.lowercased()).cooldowns")
    }

    private static func post(wav: Data, provider: String, model: String, language: String, apiKey: String) async throws -> String {
        let urls = [
            "ChatGPT": "https://api.openai.com/v1/audio/transcriptions",
            "Groq": "https://api.groq.com/openai/v1/audio/transcriptions",
            "Mistral": "https://api.mistral.ai/v1/audio/transcriptions"
        ]
        guard let endpoint = urls[provider], let url = URL(string: endpoint) else { throw BackendError.server("Unsupported speech provider.") }
        let boundary = "Phantom-\(UUID().uuidString)"
        var body = Data()
        func field(_ name: String, _ value: String) { body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n".utf8)) }
        field("model", model); field("language", language); field("response_format", "json")
        body.append(Data("--\(boundary)\r\nContent-Disposition: form-data; name=\"file\"; filename=\"speech.wav\"\r\nContent-Type: audio/wav\r\n\r\n".utf8))
        body.append(wav); body.append(Data("\r\n--\(boundary)--\r\n".utf8))
        var request = URLRequest(url: url)
        request.httpMethod = "POST"; request.timeoutInterval = 60; request.httpBody = body
        request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        request.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else { throw BackendError.http(status, "Speech provider failed (\(status)).") }
        return (try JSONDecoder().decode(SpeechText.self, from: data)).text.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func wav(_ pcm: Data) -> Data {
        var result = Data()
        result.append(Data("RIFF".utf8)); result.appendLE(UInt32(36 + pcm.count)); result.append(Data("WAVEfmt ".utf8))
        result.appendLE(UInt32(16)); result.appendLE(UInt16(1)); result.appendLE(UInt16(1)); result.appendLE(UInt32(16_000))
        result.appendLE(UInt32(32_000)); result.appendLE(UInt16(2)); result.appendLE(UInt16(16)); result.append(Data("data".utf8)); result.appendLE(UInt32(pcm.count)); result.append(pcm)
        return result
    }

    private static func containsSpeech(_ pcm: Data) -> Bool {
        let values = pcm.withUnsafeBytes { Array($0.bindMemory(to: Int16.self)) }
        guard !values.isEmpty else { return false }
        var total: Int64 = 0
        var count = 0
        for index in stride(from: 0, to: values.count, by: 8) { total += Int64(abs(Int(values[index]))); count += 1 }
        return count > 0 && total / Int64(count) >= 120
    }
}

private struct SpeechText: Decodable { let text: String }

private extension Data {
    mutating func appendLE<T: FixedWidthInteger>(_ value: T) {
        var little = value.littleEndian
        Swift.withUnsafeBytes(of: &little) { append(contentsOf: $0) }
    }
}
