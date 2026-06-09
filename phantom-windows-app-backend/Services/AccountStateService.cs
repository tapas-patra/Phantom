using System.Security.Cryptography;
using System.Text;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AccountStateService
{
    private readonly BackendOptions _options;
    private readonly AccountRepository _accounts;
    private readonly LockRepository _locks;

    public AccountStateService(BackendOptions options, AccountRepository accounts, LockRepository locks)
    {
        _options = options;
        _accounts = accounts;
        _locks = locks;
    }

    public DesktopAccountRecord GetOrCreateForLogin(AuthLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = _accounts.FindByEmail(normalizedEmail);
        if (existing != null)
        {
            if (!request.UseMagicLink && !string.IsNullOrWhiteSpace(existing.PasswordHash))
            {
                var providedHash = ComputeHash(string.IsNullOrWhiteSpace(request.Password) ? "phantom123" : request.Password);
                if (!string.Equals(existing.PasswordHash, providedHash, StringComparison.Ordinal))
                {
                    throw new BackendValidationException("Invalid credentials.");
                }
            }

            existing.UpdatedAtUtc = DateTime.UtcNow;
            existing.LastValidatedAtUtc = DateTime.UtcNow;
            _accounts.Save(existing);
            return existing;
        }

        var created = new DesktopAccountRecord
        {
            UserId = normalizedEmail,
            Email = normalizedEmail,
            PasswordHash = ComputeHash(string.IsNullOrWhiteSpace(request.Password) ? "phantom123" : request.Password),
            PhoneVerified = true,
            ProAvailableCredits = _options.DefaultProCredits,
            PremiumAvailableCredits = _options.DefaultPremiumCredits,
            PremiumNegativeCredits = 0m,
            LeaseExpiresAtUtc = DateTime.UtcNow.AddHours(_options.DefaultLeaseHours),
            OfflineModeEnabled = false,
            LastValidatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        _accounts.Save(created);
        return created;
    }

    public DesktopAccountRecord ProjectCallbackState(AuthCallbackResultDto callback)
    {
        if (string.IsNullOrWhiteSpace(callback.Email))
        {
            throw new BackendValidationException("Callback email is required.");
        }

        var normalizedEmail = callback.Email.Trim().ToLowerInvariant();
        var account = _accounts.FindByEmail(normalizedEmail) ?? new DesktopAccountRecord
        {
            UserId = normalizedEmail,
            Email = normalizedEmail,
            PasswordHash = ComputeHash("phantom123"),
            CreatedAtUtc = DateTime.UtcNow
        };

        account.PhoneVerified = callback.PhoneVerified;
        account.ProAvailableCredits = callback.Status == "nocredits" ? 0m : Math.Max(account.ProAvailableCredits, _options.DefaultProCredits);
        account.PremiumAvailableCredits = 0m;
        account.PremiumNegativeCredits = callback.Status == "negative" ? 0.5m : account.PremiumNegativeCredits;
        account.LeaseExpiresAtUtc = callback.Status == "offlineexpired"
            ? DateTime.UtcNow.AddHours(-1)
            : DateTime.UtcNow.AddHours(_options.DefaultLeaseHours);
        account.OfflineModeEnabled = callback.Status is "offlineexpired" or "resume";
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        if (account.CreatedAtUtc == default)
        {
            account.CreatedAtUtc = DateTime.UtcNow;
        }

        _accounts.Save(account);

        if (callback.Status == "resume")
        {
            _locks.Save(new DesktopLockRecord
            {
                SessionId = $"resume-{account.UserId}",
                UserId = account.UserId,
                DeviceId = string.IsNullOrWhiteSpace(callback.DeviceInstallId) ? account.UserId : callback.DeviceInstallId,
                LockToken = ComputeHash($"{account.UserId}:{callback.DeviceFingerprintHash}:resume"),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(24),
                LastHeartbeatAtUtc = DateTime.UtcNow,
                AppVersion = "callback-resume"
            });
        }

        return account;
    }

    public StartupAccountCheckResultDto BuildStartupSnapshot(string source, DesktopAccountRecord account)
    {
        var activeLock = _locks.FindActiveByUser(account.UserId);
        var hasResumableLock = activeLock != null && activeLock.ExpiresAtUtc > DateTime.UtcNow;

        return new StartupAccountCheckResultDto
        {
            UserId = account.UserId,
            Email = account.Email,
            PhoneVerified = account.PhoneVerified,
            Wallet = new WalletSnapshotDto
            {
                ProAvailableCredits = account.ProAvailableCredits,
                PremiumAvailableCredits = account.PremiumAvailableCredits,
                PremiumNegativeCredits = account.PremiumNegativeCredits
            },
            LeaseExpiresAtUtc = account.LeaseExpiresAtUtc,
            HasResumableLockedSession = hasResumableLock,
            LastLockTokenHash = hasResumableLock ? activeLock!.LockToken : string.Empty,
            LastLockedSessionId = hasResumableLock ? activeLock!.SessionId : string.Empty,
            OfflineModeEnabled = account.OfflineModeEnabled,
            LastValidatedAtUtc = account.LastValidatedAtUtc,
            Source = source
        };
    }

    public DesktopAccountRecord RequireAccount(string userId, string emailFallback = "")
    {
        var account = _accounts.FindByUserId(userId);
        if (account != null)
        {
            return account;
        }

        if (!string.IsNullOrWhiteSpace(emailFallback))
        {
            account = _accounts.FindByEmail(emailFallback);
            if (account != null)
            {
                return account;
            }
        }

        throw new BackendValidationException("Account not found.");
    }

    public void Save(DesktopAccountRecord account)
    {
        account.UpdatedAtUtc = DateTime.UtcNow;
        account.LastValidatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);
    }

    private static string ComputeHash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
