using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class AuthSessionCache
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string DeviceInstallId { get; set; } = string.Empty;
        public string DeviceFingerprintHash { get; set; } = string.Empty;
        public DateTime AuthenticatedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public string AuthMethod { get; set; } = string.Empty;
        public bool IsAuthenticated { get; set; }
    }
}
