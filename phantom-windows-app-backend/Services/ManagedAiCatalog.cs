using Phantom.WindowsApp.Backend.Contracts;

namespace Phantom.WindowsApp.Backend.Services;

public static class ManagedAiCatalog
{
    public const string ChatGpt = "ChatGPT";
    public const string Claude = "Claude";
    public const string Gemini = "Gemini";
    public const string Mistral = "Mistral";

    private static readonly IReadOnlyDictionary<string, ManagedAiProviderOptionDto> Providers =
        new Dictionary<string, ManagedAiProviderOptionDto>(StringComparer.OrdinalIgnoreCase)
        {
            [ChatGpt] = new()
            {
                ProviderId = ChatGpt,
                Label = "ChatGPT",
                Models = new[] { "gpt-4o", "gpt-4o-mini" }
            },
            [Claude] = new()
            {
                ProviderId = Claude,
                Label = "Claude",
                Models = new[] { "claude-3-5-sonnet-20241022", "claude-3-5-sonnet-20240620" }
            },
            [Gemini] = new()
            {
                ProviderId = Gemini,
                Label = "Gemini",
                Models = new[] { "gemini-2.5-flash", "gemini-2.5-pro" }
            },
            [Mistral] = new()
            {
                ProviderId = Mistral,
                Label = "Mistral",
                Models = new[] { "mistral-large-latest" }
            }
        };

    public static ManagedAiCatalogDto CreateCatalog()
    {
        return new ManagedAiCatalogDto
        {
            Providers = Providers.Values.ToArray()
        };
    }

    public static bool IsAllowedProvider(string provider)
    {
        return Providers.ContainsKey(provider);
    }

    public static bool IsAllowedModel(string provider, string model)
    {
        return Providers.TryGetValue(provider, out var option)
            && option.Models.Contains(model, StringComparer.OrdinalIgnoreCase);
    }
}
