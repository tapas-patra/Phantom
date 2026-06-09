using System;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class LocalHostedAccountClient : IHostedAccountClient
    {
        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthSessionDto session)
        {
            return new StartupAccountCheckResultDto
            {
                UserId = session.UserId,
                Email = session.Email,
                AccessTier = "pro_byo",
                PhoneVerified = true,
                Wallet = new WalletSnapshotDto
                {
                    ProAvailableCredits = 1.00m,
                    PremiumAvailableCredits = 0m,
                    PremiumNegativeCredits = 0m
                },
                LeaseExpiresAtUtc = DateTime.UtcNow.AddHours(24),
                HasResumableLockedSession = false,
                LastLockTokenHash = string.Empty,
                LastLockedSessionId = string.Empty,
                OfflineModeEnabled = false,
                LastValidatedAtUtc = DateTime.UtcNow,
                Source = "local_login"
            };
        }

        public StartupAccountCheckResultDto GetStartupAccountCheck(AuthCallbackResultDto callbackResult)
        {
            return new StartupAccountCheckResultDto
            {
                UserId = callbackResult.Email.ToLowerInvariant(),
                Email = callbackResult.Email,
                AccessTier = "pro_byo",
                PhoneVerified = callbackResult.PhoneVerified,
                Wallet = new WalletSnapshotDto
                {
                    ProAvailableCredits = callbackResult.Status == "nocredits" ? 0m : 1.00m,
                    PremiumAvailableCredits = 0m,
                    PremiumNegativeCredits = callbackResult.Status == "negative" ? 0.5m : 0m
                },
                LeaseExpiresAtUtc = callbackResult.Status == "offlineexpired" ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddHours(24),
                HasResumableLockedSession = callbackResult.Status == "resume",
                LastLockTokenHash = callbackResult.Status == "resume" ? "cached-lock-token" : string.Empty,
                LastLockedSessionId = callbackResult.Status == "resume" ? "session-local-1" : string.Empty,
                OfflineModeEnabled = callbackResult.Status == "offlineexpired" || callbackResult.Status == "resume",
                LastValidatedAtUtc = DateTime.UtcNow,
                Source = "auth_callback"
            };
        }
    }
}
