using Microsoft.AspNetCore.Http;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BrowserSessionCookieService
{
    public const string UserAccessCookie = "phantom_user_access";
    public const string UserRefreshCookie = "phantom_user_refresh";
    public const string AdminAccessCookie = "phantom_admin_access";
    public const string AdminRefreshCookie = "phantom_admin_refresh";

    private readonly BackendOptions _options;

    public BrowserSessionCookieService(BackendOptions options)
    {
        _options = options;
    }

    public void IssueUserCookies(HttpResponse response, string accessToken, string refreshToken, DateTime? accessExpiresAtUtc)
    {
        AppendCookie(response, UserAccessCookie, accessToken, accessExpiresAtUtc);
        AppendCookie(response, UserRefreshCookie, refreshToken, DateTime.UtcNow.AddDays(30));
    }

    public void IssueAdminCookies(HttpResponse response, string accessToken, string refreshToken, DateTime? accessExpiresAtUtc)
    {
        AppendCookie(response, AdminAccessCookie, accessToken, accessExpiresAtUtc);
        AppendCookie(response, AdminRefreshCookie, refreshToken, DateTime.UtcNow.AddDays(30));
    }

    public void ClearUserCookies(HttpResponse response)
    {
        DeleteCookie(response, UserAccessCookie);
        DeleteCookie(response, UserRefreshCookie);
    }

    public void ClearAdminCookies(HttpResponse response)
    {
        DeleteCookie(response, AdminAccessCookie);
        DeleteCookie(response, AdminRefreshCookie);
    }

    public string? ReadUserAccessToken(HttpRequest request) => ReadCookie(request, UserAccessCookie);
    public string? ReadUserRefreshToken(HttpRequest request) => ReadCookie(request, UserRefreshCookie);
    public string? ReadAdminAccessToken(HttpRequest request) => ReadCookie(request, AdminAccessCookie);
    public string? ReadAdminRefreshToken(HttpRequest request) => ReadCookie(request, AdminRefreshCookie);

    public string GetUserAuthorizationHeader(HttpRequest request) =>
        RequestTokenResolver.GetAuthorizationHeader(request, ReadUserAccessToken(request));

    public string GetAdminAuthorizationHeader(HttpRequest request) =>
        RequestTokenResolver.GetAuthorizationHeader(request, ReadAdminAccessToken(request));

    private static string? ReadCookie(HttpRequest request, string name)
    {
        return request.Cookies.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private void AppendCookie(HttpResponse response, string name, string value, DateTime? expiresAtUtc)
    {
        var request = response.HttpContext.Request;
        response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = request.IsHttps || !string.Equals(request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase),
            Expires = expiresAtUtc,
            Domain = string.IsNullOrWhiteSpace(_options.SharedCookieDomain) ? null : _options.SharedCookieDomain,
            Path = "/"
        });
    }

    private void DeleteCookie(HttpResponse response, string name)
    {
        var request = response.HttpContext.Request;
        response.Cookies.Delete(name, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = request.IsHttps || !string.Equals(request.Host.Host, "localhost", StringComparison.OrdinalIgnoreCase),
            Domain = string.IsNullOrWhiteSpace(_options.SharedCookieDomain) ? null : _options.SharedCookieDomain,
            Path = "/"
        });
    }
}
