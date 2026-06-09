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
    private readonly PasswordHasher _passwordHasher;

    private static readonly SeedUser[] SeedUsers =
    {
        new("free.user@phantom.app", "PhantomFree123!", 0m, 0m, true),
        new("pro.user@phantom.app", "PhantomPro123!", 5m, 0m, true),
        new("premium.user@phantom.app", "PhantomPremium123!", 5m, 5m, true)
    };

    public AccountStateService(
        BackendOptions options,
        AccountRepository accounts,
        LockRepository locks,
        PasswordHasher passwordHasher)
    {
        _options = options;
        _accounts = accounts;
        _locks = locks;
        _passwordHasher = passwordHasher;
        EnsureSeededAccounts();
    }

    public DesktopAccountRecord GetForLogin(AuthLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = _accounts.FindByEmail(normalizedEmail)
            ?? throw new BackendValidationException("Account not found.");

        if (!request.UseMagicLink)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
            {
                throw new BackendValidationException("Password is required.");
            }

            if (!_passwordHasher.Verify(request.Password, existing.PasswordHash))
            {
                throw new BackendValidationException("Invalid credentials.");
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

    private void EnsureSeededAccounts()
    {
        foreach (var seed in SeedUsers)
        {
            if (_accounts.FindByEmail(seed.Email) != null)
            {
                continue;
            }

            _accounts.Save(new DesktopAccountRecord
            {
                UserId = seed.Email,
                Email = seed.Email,
                PasswordHash = _passwordHasher.Hash(seed.Password),
                PhoneVerified = seed.PhoneVerified,
                ProAvailableCredits = seed.ProCredits,
                PremiumAvailableCredits = seed.PremiumCredits,
                PremiumNegativeCredits = 0m,
                LeaseExpiresAtUtc = DateTime.UtcNow.AddHours(_options.DefaultLeaseHours),
                OfflineModeEnabled = false,
                LastValidatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
    }

    private sealed record SeedUser(
        string Email,
        string Password,
        decimal ProCredits,
        decimal PremiumCredits,
        bool PhoneVerified);
}
