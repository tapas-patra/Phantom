enum ModelContextRegistry {
    struct Configuration { let maxContextTokens: Int; let maxResponseTokens: Int; let slidingWindowSize: Int }

    static func configuration(for model: String) -> Configuration {
        switch model {
        case "gpt-4", "gpt-3.5-turbo": return Configuration(maxContextTokens: model == "gpt-4" ? 8_000 : 4_000, maxResponseTokens: model == "gpt-4" ? 2_000 : 1_000, slidingWindowSize: model == "gpt-4" ? 5 : 3)
        case let value where value.hasPrefix("gpt-4"): return Configuration(maxContextTokens: 128_000, maxResponseTokens: 4_000, slidingWindowSize: 15)
        case let value where value.hasPrefix("claude-"): return Configuration(maxContextTokens: 200_000, maxResponseTokens: 4_000, slidingWindowSize: 15)
        case let value where value.hasPrefix("gemini-2.5-pro"): return Configuration(maxContextTokens: 2_000_000, maxResponseTokens: 8_192, slidingWindowSize: 25)
        case let value where value.hasPrefix("gemini-"): return Configuration(maxContextTokens: 1_000_000, maxResponseTokens: 8_192, slidingWindowSize: 20)
        case let value where value.hasPrefix("mistral-"): return Configuration(maxContextTokens: 32_000, maxResponseTokens: 2_000, slidingWindowSize: 10)
        case let value where value.contains("llama-3.3-70b"): return Configuration(maxContextTokens: 128_000, maxResponseTokens: 8_192, slidingWindowSize: 20)
        case let value where value.contains("llama-3.1-8b"): return Configuration(maxContextTokens: 131_072, maxResponseTokens: 8_192, slidingWindowSize: 20)
        case let value where value.contains("llama-4-scout"): return Configuration(maxContextTokens: 131_072, maxResponseTokens: 8_192, slidingWindowSize: 20)
        case let value where value.contains("compound-mini"): return Configuration(maxContextTokens: 131_072, maxResponseTokens: 8_192, slidingWindowSize: 20)
        case let value where value.contains("qwen3"): return Configuration(maxContextTokens: 131_072, maxResponseTokens: 8_192, slidingWindowSize: 20)
        default: return Configuration(maxContextTokens: 32_000, maxResponseTokens: 2_000, slidingWindowSize: 10)
        }
    }
}
