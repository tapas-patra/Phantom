using System;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HostedRuntimeOptions
    {
        public string Mode { get; init; } = "remote";
        public string WebsiteBaseUrl { get; init; } = string.Empty;
        public string DesktopBackendBaseUrl { get; init; } = string.Empty;

        public bool UseRemoteBackend => true;

        public string ModeLabel => "Hosted Backend";

        public static HostedRuntimeOptions Load()
        {
            var mode = Environment.GetEnvironmentVariable("PHANTOM_HOSTED_MODE");
            var websiteBaseUrl = Environment.GetEnvironmentVariable("PHANTOM_WEBSITE_BASE_URL");
            var backendBaseUrl = Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_BASE_URL");

            var options = new HostedRuntimeOptions
            {
                Mode = string.IsNullOrWhiteSpace(mode) ? "remote" : mode.Trim(),
                WebsiteBaseUrl = string.IsNullOrWhiteSpace(websiteBaseUrl) ? string.Empty : websiteBaseUrl.Trim().TrimEnd('/'),
                DesktopBackendBaseUrl = string.IsNullOrWhiteSpace(backendBaseUrl) ? string.Empty : backendBaseUrl.Trim().TrimEnd('/')
            };

            Validate(options);
            return options;
        }

        private static void Validate(HostedRuntimeOptions options)
        {
            if (!string.Equals(options.Mode, "remote", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Local hosted mode is no longer supported. Set PHANTOM_HOSTED_MODE=remote or remove it.");
            }

            if (string.IsNullOrWhiteSpace(options.DesktopBackendBaseUrl))
            {
                throw new InvalidOperationException(
                    "PHANTOM_WINDOWS_BACKEND_BASE_URL is required. The desktop app only runs against the hosted backend.");
            }

            if (!Uri.TryCreate(options.DesktopBackendBaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    "PHANTOM_WINDOWS_BACKEND_BASE_URL must be an absolute URL.");
            }

            if (string.IsNullOrWhiteSpace(options.WebsiteBaseUrl))
            {
                throw new InvalidOperationException(
                    "PHANTOM_WEBSITE_BASE_URL is required. The desktop app must know where your Phantom website is hosted for registration and magic-link return flows.");
            }

            if (!Uri.TryCreate(options.WebsiteBaseUrl, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    "PHANTOM_WEBSITE_BASE_URL must be an absolute URL.");
            }
        }
    }
}
