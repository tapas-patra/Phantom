namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class DeviceRegistrationMetadataDto
    {
        public string InstallId { get; set; } = string.Empty;
        public string DeviceLabel { get; set; } = string.Empty;
        public string MachineFingerprintHash { get; set; } = string.Empty;
        public string SecretFingerprintHint { get; set; } = string.Empty;
        public string AppVersion { get; set; } = string.Empty;
    }
}
