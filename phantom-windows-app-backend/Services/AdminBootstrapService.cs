using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AdminBootstrapService
{
    private readonly BackendOptions _options;
    private readonly AdminAccountRepository _admins;
    private readonly PasswordHasher _passwordHasher;
    private readonly ILogger<AdminBootstrapService> _logger;

    public AdminBootstrapService(
        BackendOptions options,
        AdminAccountRepository admins,
        PasswordHasher passwordHasher,
        ILogger<AdminBootstrapService> logger)
    {
        _options = options;
        _admins = admins;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public void EnsureBootstrapAdmin()
    {
        var bootstrapEmail = _options.BootstrapAdminEmail;
        var bootstrapPassword = _options.BootstrapAdminPassword;

        if (string.IsNullOrWhiteSpace(bootstrapEmail)
            || string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            if (string.IsNullOrWhiteSpace(_options.AdminApiKey))
            {
                return;
            }

            bootstrapEmail = "admin@phantom.local";
            bootstrapPassword = _options.AdminApiKey;
        }

        var normalizedEmail = bootstrapEmail.Trim().ToLowerInvariant();
        var existing = _admins.FindByEmail(normalizedEmail);
        if (existing != null)
        {
            return;
        }

        var now = DateTime.UtcNow;
        _admins.Save(new AdminAccountRecord
        {
            AdminId = $"admin-{Guid.NewGuid():N}",
            Email = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(_options.BootstrapAdminDisplayName)
                ? "Local Admin"
                : _options.BootstrapAdminDisplayName.Trim(),
            Role = "super_admin",
            PasswordHash = _passwordHasher.Hash(bootstrapPassword),
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        _logger.LogInformation("Bootstrapped admin account for {Email}.", normalizedEmail);
    }
}
