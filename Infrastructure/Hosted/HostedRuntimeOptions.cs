using System;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HostedRuntimeOptions
    {
        public string Mode { get; init; } = "local";
        public string WebsiteBaseUrl { get; init; } = "https://phantom.app";
        public string DesktopBackendBaseUrl { get; init; } = string.Empty;

        public bool UseRemoteBackend =>
            string.Equals(Mode, "remote", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(Mode, "auto", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(DesktopBackendBaseUrl));

        public static HostedRuntimeOptions Load()
        {
            var mode = Environment.GetEnvironmentVariable("PHANTOM_HOSTED_MODE");
            var websiteBaseUrl = Environment.GetEnvironmentVariable("PHANTOM_WEBSITE_BASE_URL");
            var backendBaseUrl = Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_BASE_URL");

            return new HostedRuntimeOptions
            {
                Mode = string.IsNullOrWhiteSpace(mode) ? "local" : mode.Trim(),
                WebsiteBaseUrl = string.IsNullOrWhiteSpace(websiteBaseUrl) ? "https://phantom.app" : websiteBaseUrl.Trim().TrimEnd('/'),
                DesktopBackendBaseUrl = string.IsNullOrWhiteSpace(backendBaseUrl) ? string.Empty : backendBaseUrl.Trim().TrimEnd('/')
            };
        }
    }
}
