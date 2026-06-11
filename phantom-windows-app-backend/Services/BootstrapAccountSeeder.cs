using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class BootstrapAccountSeeder
{
    private readonly BackendOptions _options;
    private readonly AccountRepository _accounts;
    private readonly PasswordHasher _passwordHasher;

    private static readonly BootstrapUser[] Users =
    {
        new("free.user@phantom.app", "PhantomFree123!", "free", 0m, 0m, true),
        new("pro.user@phantom.app", "PhantomPro123!", "pro_byo", 5m, 0m, true),
        new("premium.user@phantom.app", "PhantomPremium123!", "premium", 5m, 5m, true)
    };

    public BootstrapAccountSeeder(
        BackendOptions options,
        AccountRepository accounts,
        PasswordHasher passwordHasher)
    {
        _options = options;
        _accounts = accounts;
        _passwordHasher = passwordHasher;
    }

    public int SeedDefaultTestUsers()
    {
        foreach (var user in Users)
        {
            var existing = _accounts.FindByEmail(user.Email);
            _accounts.Save(new DesktopAccountRecord
            {
                UserId = existing?.UserId ?? user.Email,
                Email = user.Email,
                AccessTier = user.AccessTier,
                PasswordHash = _passwordHasher.Hash(user.Password),
                PhoneVerified = user.PhoneVerified,
                ProAvailableCredits = user.ProCredits,
                PremiumAvailableCredits = user.PremiumCredits,
                PremiumNegativeCredits = existing?.PremiumNegativeCredits ?? 0m,
                LeaseExpiresAtUtc = existing?.LeaseExpiresAtUtc ?? DateTime.UtcNow.AddHours(_options.DefaultLeaseHours),
                OfflineModeEnabled = existing?.OfflineModeEnabled ?? false,
                LastValidatedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = existing?.CreatedAtUtc ?? DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }

        return Users.Length;
    }

    private sealed record BootstrapUser(
        string Email,
        string Password,
        string AccessTier,
        decimal ProCredits,
        decimal PremiumCredits,
        bool PhoneVerified);
}
