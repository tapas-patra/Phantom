using System;
using System.Text.RegularExpressions;

namespace SecureOverlay.Services
{
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
        public const int ManagedDesktopMaxAttempts = 1;
        public const int ByoDesktopMaxAttempts = 2;

        public static ProviderFailureDecision Classify(string? error)
        {
            var value = error?.Trim().ToLowerInvariant() ?? string.Empty;
            var status = Regex.Match(value, @"(?<!\d)(401|403|408|429|500|502|503|504)(?!\d)").Value;
            if (status == "429" || ContainsAny(value, "rate_limit", "ratelimit", "too many requests", "quota", "resource_exhausted"))
                return new(ProviderFailureKind.RateLimited, "rate_limited", true, TimeSpan.FromMinutes(5));
            if (status is "401" or "403" || ContainsAny(value, "invalid api key", "invalid key", "unauthorized", "authentication"))
                return new(ProviderFailureKind.Authentication, "authentication_failed", true, TimeSpan.FromHours(24));
            if (ContainsAny(value, "cancelled", "canceled"))
                return new(ProviderFailureKind.Cancelled, "cancelled", false, TimeSpan.Zero);
            if (status is "408" or "500" or "502" or "503" or "504"
                || ContainsAny(value, "timeout", "timed out", "overloaded", "capacity", "network", "transport", "connection", "stream_incomplete", "empty response"))
                return new(ProviderFailureKind.Transient, status.Length == 3 ? $"provider_{status[0]}xx" : "provider_transport_error", true, TimeSpan.FromSeconds(30));
            return new(ProviderFailureKind.Terminal, "provider_error", false, TimeSpan.Zero);
        }

        public static bool CanRetry(ProviderFailureDecision failure, int attempt, int maxAttempts, bool hasOutput)
            => !hasOutput && attempt < maxAttempts && failure.CanRotateCredential;

        public static bool CanCrossLane(string fromLane, string toLane, bool explicitlyOptedIn, bool hasOutput)
            => string.Equals(fromLane, "byo", StringComparison.OrdinalIgnoreCase)
                && string.Equals(toLane, "managed_extension", StringComparison.OrdinalIgnoreCase)
                && explicitlyOptedIn
                && !hasOutput;

        private static bool ContainsAny(string value, params string[] terms)
            => Array.Exists(terms, value.Contains);
    }
}
