enum BYOCatalog {
    /// Known BYO provider ids only. Model lists always come from the hosted catalog.
    static let providerIds = ["ChatGPT", "Claude", "Mistral", "Gemini", "Groq", "NVIDIA"]

    static var providers: [ManagedProvider] {
        providerIds.map { ManagedProvider(providerId: $0, label: $0, models: []) }
    }
}
