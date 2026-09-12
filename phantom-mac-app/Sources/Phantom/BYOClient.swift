import Foundation

enum ReasoningContext {
    @TaskLocal static var questionType: String?
}

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
        case "OpenRouter": endpoint = URL(string: "https://openrouter.ai/api/v1/models")!
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
        if provider == "OpenRouter" {
            request.setValue("https://phantom.app", forHTTPHeaderField: "HTTP-Referer")
            request.setValue("Phantom", forHTTPHeaderField: "X-Title")
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
            let display = row["display_name"] as? String ?? row["displayName"] as? String ?? row["name"] as? String ?? id
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
        try await chatStream(
            provider: provider,
            model: model,
            apiKey: apiKey,
            imagesBase64: imageBase64.map { [$0] } ?? [],
            messages: messages
        )
    }

    func chatStream(
        provider: String,
        model: String,
        apiKey: String,
        imagesBase64: [String],
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
        case "OpenRouter": endpoint = URL(string: "https://openrouter.ai/api/v1/chat/completions")!
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
        if provider == "OpenRouter" {
            request.setValue("https://phantom.app", forHTTPHeaderField: "HTTP-Referer")
            request.setValue("Phantom", forHTTPHeaderField: "X-Title")
        }
        request.httpBody = try JSONSerialization.data(
            withJSONObject: payload(
                provider: provider,
                model: model,
                imagesBase64: imagesBase64,
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

    static func terminal(from line: String, provider: String) -> BYOStreamTerminal? {
        guard line.hasPrefix("data: ") else { return nil }
        let payload = String(line.dropFirst(6))
        if payload == "[DONE]" { return .complete }
        guard let data = payload.data(using: .utf8),
              let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        if provider == "Claude" {
            if object["type"] as? String == "message_stop" { return .complete }
            let delta = object["delta"] as? [String: Any]
            return delta?["stop_reason"] as? String == "max_tokens" ? .truncated : nil
        }
        if provider == "Gemini" {
            let reason = ((object["candidates"] as? [[String: Any]])?.first?["finishReason"] as? String)?.uppercased()
            if reason == "MAX_TOKENS" { return .truncated }
            return reason == "STOP" ? .complete : nil
        }
        let reason = ((object["choices"] as? [[String: Any]])?.first?["finish_reason"] as? String)?.lowercased()
        if reason == "length" { return .truncated }
        return reason == "stop" ? .complete : nil
    }

    private func payload(
        provider: String,
        model: String,
        imagesBase64: [String],
        messages: [ChatMessage]
    ) -> [String: Any] {
        let plan = Self.reasoningPlan(for: messages)
        let includeThinking = Self.supportsNativeThinking(provider: provider, model: model)
        if provider == "Claude" {
            var body: [String: Any] = [
                "model": model,
                "max_tokens": max(plan.maxTokens, plan.claudeThinkingTokens + 512),
                "system": messages.first(where: { $0.role == "system" })?.content ?? "",
                "messages": claudeMessages(messages, imagesBase64: imagesBase64),
                "stream": true
            ]
            if includeThinking, plan.claudeThinkingTokens > 0 {
                body["thinking"] = ["type": "enabled", "budget_tokens": plan.claudeThinkingTokens]
            }
            return body
        }
        if provider == "Gemini" {
            var generationConfig: [String: Any] = ["maxOutputTokens": plan.maxTokens, "temperature": 0.7]
            if includeThinking, plan.geminiThinkingTokens > 0 {
                generationConfig["thinkingConfig"] = [
                    "thinkingBudget": plan.geminiThinkingTokens,
                    "includeThoughts": false
                ]
            }
            return [
                "contents": geminiMessages(messages, imagesBase64: imagesBase64),
                "generationConfig": generationConfig
            ]
        }
        var body: [String: Any] = [
            "model": model,
            "messages": openAIMessages(
                messages,
                imagesBase64: imagesBase64,
                mistralImageURL: provider == "Mistral"
            ),
            "max_tokens": plan.maxTokens,
            "stream": true
        ]
        if includeThinking {
            body["reasoning"] = ["exclude": true, "effort": plan.effort]
        }
        return body
    }

    private func openAIMessages(
        _ messages: [ChatMessage],
        imagesBase64: [String],
        mistralImageURL: Bool
    ) -> [[String: Any]] {
        let valid = messages.filter { !$0.content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        let lastUserId = valid.last(where: { $0.role == "user" })?.id
        return valid.map { message in
            guard message.id == lastUserId, !imagesBase64.isEmpty else {
                return ["role": message.role, "content": message.content]
            }
            var content: [[String: Any]] = [["type": "text", "text": message.content]]
            for image in imagesBase64 {
                let imageURL: Any = mistralImageURL
                    ? "data:image/png;base64,\(image)"
                    : ["url": "data:image/png;base64,\(image)"]
                content.append(["type": "image_url", "image_url": imageURL])
            }
            return ["role": "user", "content": content]
        }
    }

    private func claudeMessages(_ messages: [ChatMessage], imagesBase64: [String]) -> [[String: Any]] {
        let valid = messages.filter { $0.role != "system" && !$0.content.isEmpty }
        let lastUserId = valid.last(where: { $0.role == "user" })?.id
        return valid.map { message in
            guard message.id == lastUserId, !imagesBase64.isEmpty else {
                return ["role": message.role, "content": message.content]
            }
            var content: [[String: Any]] = imagesBase64.map { image in
                ["type": "image", "source": ["type": "base64", "media_type": "image/png", "data": image]]
            }
            content.append(["type": "text", "text": message.content])
            return ["role": "user", "content": content]
        }
    }

    private func geminiMessages(_ messages: [ChatMessage], imagesBase64: [String]) -> [[String: Any]] {
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
            if message.id == lastUserId {
                for image in imagesBase64 {
                    parts.append(["inline_data": ["mime_type": "image/png", "data": image]])
                }
            }
            return ["role": role, "parts": parts]
        }
    }

    private static func reasoningPlan(for messages: [ChatMessage]) -> (effort: String, maxTokens: Int, claudeThinkingTokens: Int, geminiThinkingTokens: Int) {
        let effort = reasoningEffort(for: messages)
        switch effort {
        case "high": return ("high", 8_000, 5_000, 4_096)
        case "low": return ("low", 3_000, 0, 0)
        default: return ("medium", 5_000, 2_048, 1_024)
        }
    }

    private static func supportsNativeThinking(provider: String, model: String) -> Bool {
        if provider == "OpenRouter" { return true }
        let value = model.lowercased()
        let markers = [
            "o1", "o3", "o4", "gpt-5",
            "sonnet-4", "opus-4", "claude-3-7", "claude-4",
            "gemini-2.5", "gemini-3",
            "magistral", "gpt-oss", "qwq", "deepseek-r", "glm-5",
            "reasoning", "thinking"
        ]
        return markers.contains(where: { value.contains($0) })
    }

    private static func reasoningEffort(for messages: [ChatMessage]) -> String {
        if let typed = ReasoningContext.questionType?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
            switch typed {
            case "coding", "system_design", "product_case": return "high"
            case "behavioral", "personal_factual", "motivation_fit", "clarification", "factual_lookup", "status_update": return "low"
            default: break
            }
        }
        let question = messages.last(where: { $0.role == "user" })?.content.lowercased() ?? ""
        let highTerms = [
            "expand", "deeper", "in detail", "step by step", "write code", "write a function",
            "implement", "algorithm", "complexity", "debug this", "fix this", "refactor",
            "leetcode", "mermaid", "system design", "design a", "architecture", "scalability",
            "high availability", "distributed", "load balancer", "rate limiter", "microservices"
        ]
        if messages.last(where: { $0.role == "user" })?.hasCode == true
            || highTerms.contains(where: { question.contains($0) })
        {
            return "high"
        }
        let briefTerms = ["why", "how", "what about", "give an example", "clarify"]
        let wordCount = question.split(whereSeparator: { $0.isWhitespace }).count
        if wordCount <= 12 && briefTerms.contains(where: { question.contains($0) }) {
            return "low"
        }
        return "medium"
    }
}

enum BYOStreamTerminal: Equatable {
    case complete
    case truncated
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
