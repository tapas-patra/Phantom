using System;

namespace SecureOverlay.Domain.Entities
{
    public sealed class AccountCacheSnapshot
    {
        public string UserId { get; set; } = string.Empty;
        public string AccessTier { get; set; } = "free";
        public bool EmailVerified { get; set; }
        public bool PhoneVerified { get; set; }
        public decimal ProAvailableCredits { get; set; }
        public decimal PremiumAvailableCredits { get; set; }
        public decimal PremiumNegativeCredits { get; set; }
        public DateTime? LeaseExpiresAtUtc { get; set; }
        public bool HasResumableLockedSession { get; set; }
        public string LastLockTokenHash { get; set; } = string.Empty;
        public string LastLockedSessionId { get; set; } = string.Empty;
        public bool OfflineModeEnabled { get; set; }
        public DateTime LastValidatedAtUtc { get; set; }
    }
}
