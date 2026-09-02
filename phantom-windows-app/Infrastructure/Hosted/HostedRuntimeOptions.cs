using System;
using System.IO;
using System.Text.Json;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HostedRuntimeOptions
    {
        private const string DefaultWebsiteBaseUrl = "https://phantom-interview.vercel.app";
        private const string DefaultDesktopBackendBaseUrl = "https://phantom-ai-windows-app-backend.onrender.com";

        public string Mode { get; init; } = "remote";
        public string WebsiteBaseUrl { get; init; } = string.Empty;
        public string DesktopBackendBaseUrl { get; init; } = string.Empty;

        public bool UseRemoteBackend => true;

        public string ModeLabel => "Hosted Backend";

        public static HostedRuntimeOptions Load()
        {
            var fileConfig = LoadFromFile();
            var mode = ReadSetting("PHANTOM_HOSTED_MODE", fileConfig?.Mode ?? "remote");
            var websiteBaseUrl = ReadSetting("PHANTOM_WEBSITE_BASE_URL", fileConfig?.WebsiteBaseUrl ?? DefaultWebsiteBaseUrl);
            var backendBaseUrl = ReadSetting("PHANTOM_WINDOWS_BACKEND_BASE_URL", fileConfig?.DesktopBackendBaseUrl ?? DefaultDesktopBackendBaseUrl);

            var options = new HostedRuntimeOptions
            {
                Mode = string.IsNullOrWhiteSpace(mode) ? "remote" : mode.Trim(),
                WebsiteBaseUrl = string.IsNullOrWhiteSpace(websiteBaseUrl) ? string.Empty : websiteBaseUrl.Trim().TrimEnd('/'),
                DesktopBackendBaseUrl = string.IsNullOrWhiteSpace(backendBaseUrl) ? string.Empty : backendBaseUrl.Trim().TrimEnd('/')
            };

            Validate(options);
            return options;
        }

        private static string ReadSetting(string envName, string fallback)
        {
            var processValue = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrWhiteSpace(processValue))
            {
                return processValue;
            }

            var userValue = Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue))
            {
                return userValue;
            }

            var machineValue = Environment.GetEnvironmentVariable(envName, EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrWhiteSpace(machineValue))
            {
                return machineValue;
            }

            return fallback;
        }

        private static HostedRuntimeFileConfig? LoadFromFile()
        {
            var currentDirectoryPath = Path.Combine(Environment.CurrentDirectory, "phantom.hosted.json");
            if (TryLoadConfig(currentDirectoryPath, out var currentDirectoryConfig))
            {
                return currentDirectoryConfig;
            }

            var baseDirectoryPath = Path.Combine(AppContext.BaseDirectory, "phantom.hosted.json");
            if (TryLoadConfig(baseDirectoryPath, out var baseDirectoryConfig))
            {
                return baseDirectoryConfig;
            }

            return null;
        }

        private static bool TryLoadConfig(string path, out HostedRuntimeFileConfig? config)
        {
            config = null;
            if (!File.Exists(path))
            {
                return false;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            config = JsonSerializer.Deserialize<HostedRuntimeFileConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return config != null;
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

        private sealed class HostedRuntimeFileConfig
        {
            public string? Mode { get; init; }
            public string? WebsiteBaseUrl { get; init; }
            public string? DesktopBackendBaseUrl { get; init; }
        }
    }
}
