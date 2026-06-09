using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class DeviceProfile
    {
        public string InstallId { get; set; } = string.Empty;
        public string DeviceLabel { get; set; } = string.Empty;
        public string MachineFingerprintHash { get; set; } = string.Empty;
        public string SecretFingerprintHint { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public DateTime LastSeenAtUtc { get; set; }
    }
}
