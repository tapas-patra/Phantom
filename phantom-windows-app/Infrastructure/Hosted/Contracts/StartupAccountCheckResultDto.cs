using System;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class StartupAccountCheckResultDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string AccessTier { get; set; } = "free";
        public bool PhoneVerified { get; set; }
        public WalletSnapshotDto Wallet { get; set; } = new WalletSnapshotDto();
        public DateTime? LeaseExpiresAtUtc { get; set; }
        public bool HasResumableLockedSession { get; set; }
        public string LastLockTokenHash { get; set; } = string.Empty;
        public string LastLockedSessionId { get; set; } = string.Empty;
        public bool OfflineModeEnabled { get; set; }
        public DateTime LastValidatedAtUtc { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
