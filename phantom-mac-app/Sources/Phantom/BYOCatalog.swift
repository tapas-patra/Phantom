enum BYOCatalog {
    static let providers: [ManagedProvider] = [
        provider("ChatGPT"),
        provider("Claude"),
        provider("Mistral"),
        provider("Gemini"),
        provider("Groq"),
        provider("NVIDIA")
    ]

    private static func provider(_ name: String) -> ManagedProvider {
        ManagedProvider(providerId: name, label: name, models: [])
    }
}
