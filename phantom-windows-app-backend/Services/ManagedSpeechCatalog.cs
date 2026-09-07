namespace Phantom.WindowsApp.Backend.Services;

public static class ManagedSpeechCatalog
{
    static ManagedSpeechCatalog()
    {
        if (!LooksLikeSpeechModel(ManagedAiCatalog.ChatGpt, "gpt-4o-transcribe")
            || LooksLikeSpeechModel(ManagedAiCatalog.ChatGpt, "gpt-4o")
            || LooksLikeSpeechModel(ManagedAiCatalog.Mistral, "voxtral-mini-transcribe-realtime-2602"))
            throw new InvalidOperationException("Managed speech model filter self-check failed.");
    }
    private static readonly IReadOnlyDictionary<string, string> Providers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ManagedAiCatalog.ChatGpt] = "OpenAI",
            [ManagedAiCatalog.Groq] = "Groq",
            [ManagedAiCatalog.Mistral] = "Mistral"
        };

    public static bool IsAllowedProvider(string provider) => Providers.ContainsKey(provider);
    public static string GetProviderLabel(string provider) => Providers.TryGetValue(provider, out var label) ? label : provider;
    public static IReadOnlyList<string> GetAllProviders() => Providers.Keys.ToArray();

    public static string GetModelsUrl(string provider) => provider switch
    {
        ManagedAiCatalog.ChatGpt => "https://api.openai.com/v1/models",
        ManagedAiCatalog.Groq => "https://api.groq.com/openai/v1/models",
        ManagedAiCatalog.Mistral => "https://api.mistral.ai/v1/models",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public static string GetTranscriptionUrl(string provider) => provider switch
    {
        ManagedAiCatalog.ChatGpt => "https://api.openai.com/v1/audio/transcriptions",
        ManagedAiCatalog.Groq => "https://api.groq.com/openai/v1/audio/transcriptions",
        ManagedAiCatalog.Mistral => "https://api.mistral.ai/v1/audio/transcriptions",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    public static bool LooksLikeSpeechModel(string provider, string modelId)
    {
        var id = modelId.ToLowerInvariant();
        return provider switch
        {
            ManagedAiCatalog.ChatGpt => (id.Contains("transcribe") || id.StartsWith("whisper")) && !id.Contains("live") && !id.Contains("realtime"),
            ManagedAiCatalog.Groq => id.Contains("whisper"),
            ManagedAiCatalog.Mistral => id.Contains("voxtral") && id.Contains("transcribe") && !id.Contains("realtime"),
            _ => false
        };
    }
}
