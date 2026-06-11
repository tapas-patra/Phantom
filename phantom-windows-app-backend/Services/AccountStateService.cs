using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AccountStateService
{
    private readonly AccountRepository _accounts;
    private readonly LockRepository _locks;
    private readonly PasswordHasher _passwordHasher;

    public AccountStateService(
        AccountRepository accounts,
        LockRepository locks,
        PasswordHasher passwordHasher)
    {
        _accounts = accounts;
        _locks = locks;
        _passwordHasher = passwordHasher;
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
            throw new BackendValidationException(request.UseMagicLink
                ? "Account not found."
                : "Invalid email or password.");
        }

        if (!request.UseMagicLink)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new BackendValidationException("Password is required.");
            }

            if (!_passwordHasher.Verify(request.Password, existing.PasswordHash))
            {
                throw new BackendValidationException("Invalid email or password.");
            }
        }

        existing.UpdatedAtUtc = DateTime.UtcNow;
        existing.LastValidatedAtUtc = DateTime.UtcNow;
        _accounts.Save(existing);
        return existing;
    }

    public StartupAccountCheckResultDto BuildStartupSnapshot(string source, DesktopAccountRecord account)
    {
        var activeLock = _locks.FindActiveByUser(account.UserId);
        var hasResumableLock = activeLock != null && activeLock.ExpiresAtUtc > DateTime.UtcNow;

        return new StartupAccountCheckResultDto
        {
            UserId = account.UserId,
            Email = account.Email,
            AccessTier = account.AccessTier,
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
}
