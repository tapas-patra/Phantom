using Phantom.WindowsApp.Backend.Contracts;

namespace Phantom.WindowsApp.Backend.Services;

public static class ManagedAiCatalog
{
    public const string ChatGpt = "ChatGPT";
    public const string Claude = "Claude";
    public const string Gemini = "Gemini";
    public const string Mistral = "Mistral";
    public const string Nvidia = "NVIDIA";

    private static readonly IReadOnlyDictionary<string, string> ProviderLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ChatGpt] = "ChatGPT",
            [Claude] = "Claude",
            [Gemini] = "Gemini",
            [Mistral] = "Mistral",
            [Nvidia] = "NVIDIA"
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
}
