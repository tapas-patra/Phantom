using Phantom.WindowsApp.Backend.Contracts;

namespace Phantom.WindowsApp.Backend.Services;

public static class ManagedAiCatalog
{
    public const string ChatGpt = "ChatGPT";
    public const string Claude = "Claude";
    public const string Gemini = "Gemini";
    public const string Mistral = "Mistral";
    public const string Groq = "Groq";
    public const string Nvidia = "NVIDIA";
    public const string OpenRouter = "OpenRouter";

    public const string OpenRouterChatCompletionsUrl = "https://openrouter.ai/api/v1/chat/completions";
    public const string OpenRouterModelsUrl = "https://openrouter.ai/api/v1/models";

    private static readonly IReadOnlyDictionary<string, string> ProviderLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ChatGpt] = "ChatGPT",
            [Claude] = "Claude",
            [Gemini] = "Gemini",
            [Mistral] = "Mistral",
            [Groq] = "Groq",
            [Nvidia] = "NVIDIA",
            [OpenRouter] = "OpenRouter"
        };

    public static bool IsAllowedProvider(string provider)
    {
        return ProviderLabels.ContainsKey(provider);
    }

    public static string GetProviderLabel(string provider)
    {
        return ProviderLabels.TryGetValue(provider, out var label) ? label : provider;
    }

    public static IReadOnlyList<string> GetAllProviders()
    {
        return ProviderLabels.Keys.ToArray();
    }

    public static bool IsOpenRouter(string provider)
    {
        return string.Equals(provider, OpenRouter, StringComparison.OrdinalIgnoreCase);
    }

    public static void ApplyOpenRouterHeaders(System.Net.Http.Headers.HttpRequestHeaders headers)
    {
        headers.TryAddWithoutValidation("HTTP-Referer", "https://phantom.app");
        headers.TryAddWithoutValidation("X-Title", "Phantom");
    }
}
