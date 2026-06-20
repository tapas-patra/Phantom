using Microsoft.AspNetCore.Http;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class BrowserSessionCookieService
{
    public const string UserAccessCookie = "phantom_user_access";
    public const string AdminAccessCookie = "phantom_admin_access";

    public string GetUserAuthorizationHeader(HttpRequest request) =>
        RequestTokenResolver.GetAuthorizationHeader(request, ReadCookie(request, UserAccessCookie));

    public string GetAdminAuthorizationHeader(HttpRequest request) =>
        RequestTokenResolver.GetAuthorizationHeader(request, ReadCookie(request, AdminAccessCookie));

    private static string? ReadCookie(HttpRequest request, string name)
    {
        return request.Cookies.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }
}
