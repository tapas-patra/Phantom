using Microsoft.Extensions.Configuration;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class DashboardOptions
{
    public string DatabaseUrl { get; init; } = string.Empty;
    public string AdminApiKey { get; init; } = string.Empty;
    public string WindowsBackendBaseUrl { get; init; } = string.Empty;
    public string WindowsBackendAdminApiKey { get; init; } = string.Empty;

    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);
    public bool HasWindowsBackendAdminAccess => !string.IsNullOrWhiteSpace(WindowsBackendBaseUrl);

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
            WindowsBackendAdminApiKey =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY")?.Trim()
                ?? section["WindowsBackendAdminApiKey"]?.Trim()
                ?? string.Empty
        };
    }
}
