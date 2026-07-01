using Microsoft.Extensions.Configuration;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class DashboardOptions
{
    public string DatabaseUrl { get; init; } = string.Empty;
    public string AdminApiKey { get; init; } = string.Empty;
    public string WindowsBackendBaseUrl { get; init; } = string.Empty;
    public string WindowsBackendInternalApiKey { get; init; } = string.Empty;
    public string PublicWebsiteBaseUrl { get; init; } = string.Empty;
    public string SharedCookieDomain { get; init; } = string.Empty;

    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);
    public bool HasWindowsBackendAdminAccess => !string.IsNullOrWhiteSpace(WindowsBackendBaseUrl);
    public bool HasWindowsBackendInternalAccess =>
        !string.IsNullOrWhiteSpace(WindowsBackendBaseUrl)
        && !string.IsNullOrWhiteSpace(WindowsBackendInternalApiKey);

    public static DashboardOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("PhantomDashboard");
        return new DashboardOptions
        {
            DatabaseUrl =
                Environment.GetEnvironmentVariable("PHANTOM_DASHBOARD_BACKEND_DATABASE_URL")?.Trim()
                ?? Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DATABASE_URL")?.Trim()
                ?? section["DatabaseUrl"]?.Trim()
                ?? string.Empty,
            AdminApiKey =
                Environment.GetEnvironmentVariable("PHANTOM_DASHBOARD_ADMIN_API_KEY")?.Trim()
                ?? section["AdminApiKey"]?.Trim()
                ?? string.Empty,
            WindowsBackendBaseUrl =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_BASE_URL")?.Trim().TrimEnd('/')
                ?? section["WindowsBackendBaseUrl"]?.Trim().TrimEnd('/')
                ?? string.Empty,
            WindowsBackendInternalApiKey =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY")?.Trim()
                ?? section["WindowsBackendInternalApiKey"]?.Trim()
                ?? string.Empty,
            PublicWebsiteBaseUrl =
                Environment.GetEnvironmentVariable("PHANTOM_PUBLIC_WEBSITE_BASE_URL")?.Trim().TrimEnd('/')
                ?? Environment.GetEnvironmentVariable("PHANTOM_WEBSITE_BASE_URL")?.Trim().TrimEnd('/')
                ?? section["PublicWebsiteBaseUrl"]?.Trim().TrimEnd('/')
                ?? string.Empty,
            SharedCookieDomain =
                Environment.GetEnvironmentVariable("PHANTOM_SHARED_COOKIE_DOMAIN")?.Trim()
                ?? section["SharedCookieDomain"]?.Trim()
                ?? string.Empty
        };
    }
}
