using Microsoft.Extensions.Configuration;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class DashboardOptions
{
    public const string DefaultWindowsBackendBaseUrl = "https://phantom-ai-windows-app-backend.onrender.com";
    public const string DefaultPublicWebsiteBaseUrl = "https://phantom-interview.vercel.app";

    public string DatabaseUrl { get; init; } = string.Empty;
    public string AdminApiKey { get; init; } = string.Empty;
    public string WindowsBackendBaseUrl { get; init; } = string.Empty;
    public string WindowsBackendInternalApiKey { get; init; } = string.Empty;
    public string PublicWebsiteBaseUrl { get; init; } = string.Empty;
    public string SharedCookieDomain { get; init; } = string.Empty;
    public string ReleaseRepository { get; init; } = "tapas-patra/phantom-release-repo";
    public string ReleaseTag { get; init; } = "desktop-latest";
    public bool TrustForwardedHeaders { get; init; }

    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);
    public bool HasWindowsBackendAdminAccess => !string.IsNullOrWhiteSpace(WindowsBackendBaseUrl);
    public bool HasWindowsBackendInternalAccess =>
        !string.IsNullOrWhiteSpace(WindowsBackendBaseUrl)
        && !string.IsNullOrWhiteSpace(WindowsBackendInternalApiKey);

    public void ValidateForProduction()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DatabaseUrl)) errors.Add($"{nameof(DatabaseUrl)} is required.");
        if (string.IsNullOrWhiteSpace(WindowsBackendInternalApiKey) || WindowsBackendInternalApiKey.Length < 32)
        {
            errors.Add($"{nameof(WindowsBackendInternalApiKey)} must contain at least 32 characters.");
        }
        ValidateHttpsUrl(errors, WindowsBackendBaseUrl, nameof(WindowsBackendBaseUrl));
        ValidateHttpsUrl(errors, PublicWebsiteBaseUrl, nameof(PublicWebsiteBaseUrl));
        if (!TrustForwardedHeaders) errors.Add($"{nameof(TrustForwardedHeaders)} must be true when production runs behind the Render reverse proxy.");
        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Unsafe production configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
        }
    }

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
                ?? DefaultWindowsBackendBaseUrl,
            WindowsBackendInternalApiKey =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY")?.Trim()
                ?? section["WindowsBackendInternalApiKey"]?.Trim()
                ?? string.Empty,
            PublicWebsiteBaseUrl =
                Environment.GetEnvironmentVariable("PHANTOM_PUBLIC_WEBSITE_BASE_URL")?.Trim().TrimEnd('/')
                ?? Environment.GetEnvironmentVariable("PHANTOM_WEBSITE_BASE_URL")?.Trim().TrimEnd('/')
                ?? section["PublicWebsiteBaseUrl"]?.Trim().TrimEnd('/')
                ?? DefaultPublicWebsiteBaseUrl,
            SharedCookieDomain =
                Environment.GetEnvironmentVariable("PHANTOM_SHARED_COOKIE_DOMAIN")?.Trim()
                ?? section["SharedCookieDomain"]?.Trim()
                ?? string.Empty,
            ReleaseRepository =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_RELEASE_REPOSITORY")?.Trim()
                ?? section["ReleaseRepository"]?.Trim()
                ?? "tapas-patra/phantom-release-repo",
            ReleaseTag =
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_RELEASE_TAG")?.Trim()
                ?? section["ReleaseTag"]?.Trim()
                ?? "desktop-latest",
            TrustForwardedHeaders = bool.TryParse(
                Environment.GetEnvironmentVariable("PHANTOM_DASHBOARD_BACKEND_TRUST_FORWARDED_HEADERS")
                    ?? section["TrustForwardedHeaders"],
                out var trustForwardedHeaders) && trustForwardedHeaders
        };
    }

    private static void ValidateHttpsUrl(ICollection<string> errors, string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var url)
            || !string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{name} must be an absolute HTTPS URL.");
        }
    }
}
