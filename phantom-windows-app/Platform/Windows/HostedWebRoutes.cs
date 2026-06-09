using System;
using SecureOverlay.Infrastructure.Hosted;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Platform.Windows
{
    public static class HostedWebRoutes
    {
        public static string BaseUrl => HostedClientFactory.LoadOptions().WebsiteBaseUrl;

        public static string BuildRegisterUrl(DeviceRegistrationMetadataDto metadata)
        {
            return
                $"{BaseUrl}/register?source=desktop" +
                $"&appVersion={Uri.EscapeDataString(metadata.AppVersion)}" +
                $"&installId={Uri.EscapeDataString(metadata.InstallId)}" +
                $"&deviceLabel={Uri.EscapeDataString(metadata.DeviceLabel)}" +
                $"&deviceFingerprint={Uri.EscapeDataString(metadata.MachineFingerprintHash)}" +
                $"&deviceHint={Uri.EscapeDataString(metadata.SecretFingerprintHint)}";
        }
    }
}
