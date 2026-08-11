import Foundation

struct BYOClient {
    func models(provider: String, apiKey: String) async throws -> [ManagedModel] {
        let endpoint: URL
        switch provider {
        case "ChatGPT": endpoint = URL(string: "https://api.openai.com/v1/models")!
        case "Claude": endpoint = URL(string: "https://api.anthropic.com/v1/models")!
        case "Gemini": endpoint = URL(string: "https://generativelanguage.googleapis.com/v1beta/models?key=\(apiKey)")!
        case "Mistral": endpoint = URL(string: "https://api.mistral.ai/v1/models")!
        case "Groq": endpoint = URL(string: "https://api.groq.com/openai/v1/models")!
        case "NVIDIA": endpoint = URL(string: "https://integrate.api.nvidia.com/v1/models")!
        default: throw BYOError.unsupportedProvider
        }
        var request = URLRequest(url: endpoint)
        request.timeoutInterval = 20
        if provider == "Claude" {
            request.setValue(apiKey, forHTTPHeaderField: "x-api-key")
            request.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
        } else if provider != "Gemini" {
            request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        }
        let (data, response) = try await URLSession.shared.data(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            throw BYOError.server(status: status, message: String(data: data, encoding: .utf8) ?? "Model catalog request failed.")
        }
        let object = try JSONSerialization.jsonObject(with: data) as? [String: Any]
        let rows = (provider == "Gemini" ? object?["models"] : object?["data"]) as? [[String: Any]] ?? []
        return rows.compactMap { row in
            var id = row["id"] as? String ?? row["name"] as? String ?? ""
            id = id.replacingOccurrences(of: "models/", with: "")
            let methods = row["supportedGenerationMethods"] as? [String] ?? []
            guard Self.isChatModel(id), provider != "Gemini" || methods.isEmpty || methods.contains("generateContent") else { return nil }
            let display = row["display_name"] as? String ?? row["displayName"] as? String ?? id
            return ManagedModel(modelId: id, displayName: display, supportsVision: Self.supportsVision(id))
        }.sorted { $0.displayName.localizedCaseInsensitiveCompare($1.displayName) == .orderedAscending }
    }

    private static func isChatModel(_ id: String) -> Bool {
        let value = id.lowercased()
        guard !value.isEmpty else { return false }
        return !["embedding", "moderation", "whisper", "tts", "image", "dall-e", "rerank", "guard", "audio", "realtime"].contains(where: value.contains)
    }

    private static func supportsVision(_ id: String) -> Bool {
        let value = id.lowercased()
        return ["vision", "gpt-4o", "gpt-4.1", "gemini", "claude-3", "claude-sonnet-4", "pixtral", "llama-4-scout", "vl"].contains(where: value.contains)
    }

    func chatStream(
        provider: String,
        model: String,
        apiKey: String,
        imageBase64: String?,
        messages: [ChatMessage]
    ) async throws -> URLSession.AsyncBytes {
        let endpoint: URL
        switch provider {
        case "ChatGPT": endpoint = URL(string: "https://api.openai.com/v1/chat/completions")!
        case "Claude": endpoint = URL(string: "https://api.anthropic.com/v1/messages")!
        case "Gemini":
            var components = URLComponents(string: "https://generativelanguage.googleapis.com/v1beta/models/\(model):streamGenerateContent")!
            components.queryItems = [URLQueryItem(name: "key", value: apiKey), URLQueryItem(name: "alt", value: "sse")]
            endpoint = components.url!
        case "Mistral": endpoint = URL(string: "https://api.mistral.ai/v1/chat/completions")!
        case "Groq": endpoint = URL(string: "https://api.groq.com/openai/v1/chat/completions")!
        case "NVIDIA": endpoint = URL(string: "https://integrate.api.nvidia.com/v1/chat/completions")!
        default: throw BYOError.unsupportedProvider
        }

        var request = URLRequest(url: endpoint)
        request.httpMethod = "POST"
        request.timeoutInterval = 300
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if provider == "Claude" {
            request.setValue(apiKey, forHTTPHeaderField: "x-api-key")
            request.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
        } else if provider != "Gemini" {
            request.setValue("Bearer \(apiKey)", forHTTPHeaderField: "Authorization")
        }
        request.httpBody = try JSONSerialization.data(
            withJSONObject: payload(
                provider: provider,
                model: model,
                imageBase64: imageBase64,
                messages: messages
            )
        )

        let (bytes, response) = try await URLSession.shared.bytes(for: request)
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        guard (200..<300).contains(status) else {
            var data = Data()
            for try await byte in bytes { data.append(byte) }
            throw BYOError.server(
                status: status,
                message: String(data: data, encoding: .utf8) ?? "Provider request failed."
            )
        }
        return bytes
    }

    static func delta(from line: String, provider: String) -> String? {
        guard line.hasPrefix("data: ") else { return nil }
        let payload = String(line.dropFirst(6))
        guard payload != "[DONE]",
              let data = payload.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }

        if provider == "Claude" {
            guard object["type"] as? String == "content_block_delta",
                  let delta = object["delta"] as? [String: Any] else { return nil }
            return delta["text"] as? String
        }
        if provider == "Gemini" {
            let candidates = object["candidates"] as? [[String: Any]]
            let content = candidates?.first?["content"] as? [String: Any]
            let parts = content?["parts"] as? [[String: Any]]
            return parts?.first?["text"] as? String
        }
        let choices = object["choices"] as? [[String: Any]]
        let delta = choices?.first?["delta"] as? [String: Any]
        return delta?["content"] as? String
    }

    static func failure(from line: String) -> String? {
        guard line.hasPrefix("data: "),
              let data = String(line.dropFirst(6)).data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let error = object["error"] else { return nil }
        if let message = error as? String { return message }
        if let value = error as? [String: Any] { return value["message"] as? String ?? String(describing: value) }
        return String(describing: error)
    }

    private func payload(
        provider: String,
        model: String,
        imageBase64: String?,
        messages: [ChatMessage]
    ) -> [String: Any] {
        if provider == "Claude" {
            return [
                "model": model,
                "max_tokens": 2_000,
                "system": messages.first(where: { $0.role == "system" })?.content ?? "",
                "messages": claudeMessages(messages, imageBase64: imageBase64),
                "stream": true
            ]
        }
        if provider == "Gemini" {
            return [
                "contents": geminiMessages(messages, imageBase64: imageBase64),
                "generationConfig": ["maxOutputTokens": 2_000, "temperature": 0.7]
            ]
        }
        return [
            "model": model,
            "messages": openAIMessages(
                messages,
                imageBase64: imageBase64,
                mistralImageURL: provider == "Mistral"
            ),
            "max_tokens": 2_000,
            "stream": true
        ]
    }

    private func openAIMessages(
        _ messages: [ChatMessage],
        imageBase64: String?,
        mistralImageURL: Bool
    ) -> [[String: Any]] {
        let valid = messages.filter { !$0.content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        let lastUserId = valid.last(where: { $0.role == "user" })?.id
        return valid.map { message in
            guard message.id == lastUserId, let imageBase64 else {
                return ["role": message.role, "content": message.content]
            }
            let imageURL: Any = mistralImageURL
                ? "data:image/png;base64,\(imageBase64)"
                : ["url": "data:image/png;base64,\(imageBase64)"]
            return [
                "role": "user",
                "content": [
                    ["type": "text", "text": message.content],
                    ["type": "image_url", "image_url": imageURL]
                ]
            ]
        }
    }

    private func claudeMessages(_ messages: [ChatMessage], imageBase64: String?) -> [[String: Any]] {
        let valid = messages.filter { $0.role != "system" && !$0.content.isEmpty }
        let lastUserId = valid.last(where: { $0.role == "user" })?.id
        return valid.map { message in
            guard message.id == lastUserId, let imageBase64 else {
                return ["role": message.role, "content": message.content]
            }
            return [
                "role": "user",
                "content": [
                    ["type": "image", "source": ["type": "base64", "media_type": "image/png", "data": imageBase64]],
                    ["type": "text", "text": message.content]
                ]
            ]
        }
    }

    private func geminiMessages(_ messages: [ChatMessage], imageBase64: String?) -> [[String: Any]] {
        let system = messages.first(where: { $0.role == "system" })?.content ?? ""
        let valid = messages.filter { $0.role != "system" && !$0.content.isEmpty }
        let firstUserId = valid.first(where: { $0.role == "user" })?.id
        let lastUserId = valid.last(where: { $0.role == "user" })?.id
        return valid.map { message in
            let role = message.role == "assistant" ? "model" : "user"
            let text = message.id == firstUserId && !system.isEmpty
                ? system + "\n\n" + message.content
                : message.content
            var parts: [[String: Any]] = [["text": text]]
            if message.id == lastUserId, let imageBase64 {
                parts.append(["inline_data": ["mime_type": "image/png", "data": imageBase64]])
            }
            return ["role": role, "parts": parts]
        }
    }
}

enum BYOError: LocalizedError {
    case unsupportedProvider
    case server(status: Int, message: String)

    var statusCode: Int? {
        if case .server(let status, _) = self { return status }
        return nil
    }

    var errorDescription: String? {
        switch self {
        case .unsupportedProvider: return "The selected BYO provider is unsupported."
        case .server(_, let message): return message
        }
    }
}
