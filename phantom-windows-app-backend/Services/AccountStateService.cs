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
    private const string SeededTestEmail = "test.user@phantom.app";
    private const string SeededTestPassword = "Phantom123!";

    public AccountStateService(BackendOptions options, AccountRepository accounts, LockRepository locks)
    {
        _options = options;
        _accounts = accounts;
        _locks = locks;
        EnsureSeededAccounts();
    }

    public DesktopAccountRecord GetForLogin(AuthLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = _accounts.FindByEmail(normalizedEmail);
        if (existing == null)
        {
            throw new BackendValidationException("Account not found.");
        }

        if (!request.UseMagicLink && !string.IsNullOrWhiteSpace(existing.PasswordHash))
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new BackendValidationException("Password is required.");
            }

            var providedHash = ComputeHash(request.Password);
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

    public DesktopAccountRecord ProjectCallbackState(AuthCallbackResultDto callback)
    {
        if (string.IsNullOrWhiteSpace(callback.Email))
        {
            throw new BackendValidationException("Callback email is required.");
        }

        var normalizedEmail = callback.Email.Trim().ToLowerInvariant();
        var account = _accounts.FindByEmail(normalizedEmail)
            ?? throw new BackendValidationException("Account not found.");

        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;

        _accounts.Save(account);

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

    public DesktopAccountRecord RequireAccountByEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BackendValidationException("Email is required.");
        }

        return _accounts.FindByEmail(email.Trim().ToLowerInvariant())
            ?? throw new BackendValidationException("Account not found.");
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

    private void EnsureSeededAccounts()
    {
        if (_accounts.FindByEmail(SeededTestEmail) != null)
        {
            return;
        }

        _accounts.Save(new DesktopAccountRecord
        {
            UserId = SeededTestEmail,
            Email = SeededTestEmail,
            PasswordHash = ComputeHash(SeededTestPassword),
            PhoneVerified = true,
            ProAvailableCredits = 5m,
            PremiumAvailableCredits = 0m,
            PremiumNegativeCredits = 0m,
            LeaseExpiresAtUtc = DateTime.UtcNow.AddHours(_options.DefaultLeaseHours),
            OfflineModeEnabled = false,
            LastValidatedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
    }
}
