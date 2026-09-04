namespace Phantom.WindowsApp.Backend.Services;

public enum ProviderFailureKind
{
    RateLimited,
    Authentication,
    Transient,
    Terminal,
    Cancelled
}

public readonly record struct ProviderFailureDecision(
    ProviderFailureKind Kind,
    string ErrorCode,
    bool CanRotateCredential,
    TimeSpan Cooldown);

public static class ProviderResiliencePolicy
{
    public const int ManagedBackendMaxAttempts = 2;

    public static ProviderFailureDecision Classify(Exception error)
    {
        if (error is OperationCanceledException)
            return new(ProviderFailureKind.Cancelled, "cancelled", false, TimeSpan.Zero);
        if (error is ManagedAiProviderException provider)
            return provider.ProviderStatusCode.HasValue
                ? FromStatus(provider.ProviderStatusCode, provider.Code)
                : FromMessage(provider.Code);
        if (error is TimeoutException)
            return new(ProviderFailureKind.Transient, "first_token_timeout", true, TimeSpan.FromSeconds(30));
        if (error is HttpRequestException)
            return new(ProviderFailureKind.Transient, "provider_network_error", true, TimeSpan.FromSeconds(30));
        return FromMessage(error.Message);
    }

    public static ProviderFailureDecision FromStatus(int? statusCode, string fallbackCode = "provider_error")
        => statusCode switch
        {
            429 => new(ProviderFailureKind.RateLimited, "rate_limited", true, TimeSpan.FromMinutes(5)),
            401 or 403 => new(ProviderFailureKind.Authentication, "authentication_failed", true, TimeSpan.FromHours(24)),
            408 or 500 or 502 or 503 or 504 => new(ProviderFailureKind.Transient, $"provider_{statusCode / 100}xx", true, TimeSpan.FromSeconds(30)),
            _ => new(ProviderFailureKind.Terminal, fallbackCode, false, TimeSpan.Zero)
        };

    private static ProviderFailureDecision FromMessage(string? message)
    {
        var value = message?.Trim().ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(value, "rate_limit", "ratelimit", "too many requests", "quota", "resource_exhausted"))
            return new(ProviderFailureKind.RateLimited, "rate_limited", true, TimeSpan.FromMinutes(5));
        if (ContainsAny(value, "invalid api key", "invalid key", "unauthorized", "authentication"))
            return new(ProviderFailureKind.Authentication, "authentication_failed", true, TimeSpan.FromHours(24));
        if (ContainsAny(value, "cancelled", "canceled"))
            return new(ProviderFailureKind.Cancelled, "cancelled", false, TimeSpan.Zero);
        if (ContainsAny(value, "timeout", "timed out", "overloaded", "capacity", "network", "transport", "connection", "provider_stream_incomplete", "empty response"))
            return new(ProviderFailureKind.Transient, value.Contains("provider_stream_incomplete", StringComparison.Ordinal) ? "provider_stream_incomplete" : "provider_transport_error", true, TimeSpan.FromSeconds(30));
        return new(ProviderFailureKind.Terminal, "provider_error", false, TimeSpan.Zero);
    }

    public static bool CanRetry(ProviderFailureDecision failure, int attempt, bool hasOutput)
        => !hasOutput && attempt < ManagedBackendMaxAttempts && failure.CanRotateCredential;

    private static bool ContainsAny(string value, params string[] terms)
        => Array.Exists(terms, value.Contains);
}
