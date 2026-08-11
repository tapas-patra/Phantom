enum BYOCatalog {
    static let providers: [ManagedProvider] = [
        provider("ChatGPT", [
            model("gpt-4o", "GPT-4o", true),
            model("gpt-4o-mini", "GPT-4o Mini", true),
            model("gpt-4-turbo", "GPT-4 Turbo", true),
            model("gpt-4-vision-preview", "GPT-4 Vision", true),
            model("gpt-4", "GPT-4", false),
            model("gpt-3.5-turbo", "GPT-3.5 Turbo", false)
        ]),
        provider("Claude", [
            model("claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet", true),
            model("claude-3-5-sonnet-20240620", "Claude 3.5 Sonnet (Jun)", true),
            model("claude-3-opus-20240229", "Claude 3 Opus", true),
            model("claude-3-sonnet-20240229", "Claude 3 Sonnet", true),
            model("claude-3-haiku-20240307", "Claude 3 Haiku", true)
        ]),
        provider("Mistral", [
            model("mistral-large-latest", "Mistral Large", true),
            model("mistral-medium-latest", "Mistral Medium", false),
            model("mistral-small-latest", "Mistral Small", false)
        ]),
        provider("Gemini", [
            model("gemini-2.5-pro", "Gemini 2.5 Pro", true),
            model("gemini-2.5-flash", "Gemini 2.5 Flash", true),
            model("gemini-2.0-flash-exp", "Gemini 2.0 Flash", true)
        ]),
        provider("Groq", [
            model("llama-3.3-70b-versatile", "Llama 3.3 70B", false),
            model("llama-3.1-8b-instant", "Llama 3.1 8B", false),
            model("meta-llama/llama-4-scout-17b-16e-instruct", "Llama 4 Scout", true),
            model("groq/compound-mini", "Compound Mini", false),
            model("qwen/qwen3-32b", "Qwen3 32B", false)
        ]),
        provider("NVIDIA", [
            model("meta/llama-3.3-70b-instruct", "Llama 3.3 70B", false),
            model("meta/llama-4-scout-17b-16e-instruct", "Llama 4 Scout", true),
            model("nvidia/llama-3.1-nemotron-ultra-253b-v1", "Nemotron Ultra 253B", false),
            model("qwen/qwen3-235b-a22b", "Qwen3 235B", false)
        ])
    ]

    private static func provider(_ name: String, _ models: [ManagedModel]) -> ManagedProvider {
        ManagedProvider(providerId: name, label: name, models: models)
    }

    private static func model(_ id: String, _ name: String, _ vision: Bool) -> ManagedModel {
        ManagedModel(modelId: id, displayName: name, supportsVision: vision)
    }
}
