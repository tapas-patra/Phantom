using Phantom.WindowsApp.Backend.Services;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    public const string AdminContextKey = "Phantom.AuthenticatedAdmin";
    private readonly AdminAuthService _adminAuth;
    private readonly BrowserSessionCookieService _cookies;

    public AdminApiKeyFilter(AdminAuthService adminAuth, BrowserSessionCookieService cookies)
    {
        _adminAuth = adminAuth;
        _cookies = cookies;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var authorizationHeader = _cookies.GetAdminAuthorizationHeader(request);
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            try
            {
                var admin = _adminAuth.RequireAdminFromAuthorization(authorizationHeader);
                context.HttpContext.Items[AdminContextKey] = admin;
                if (!CanAccess(admin.Role, request.Method, request.Path))
                {
                    return Results.Forbid();
                }
                return await next(context);
            }
            catch (BackendValidationException)
            {
                return Results.Unauthorized();
            }
        }

        return Results.Unauthorized();
    }

    private static bool CanAccess(string role, string method, PathString path)
    {
        if (string.Equals(role, "super_admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return true;
        }

        return string.Equals(role, "support_admin", StringComparison.OrdinalIgnoreCase)
            && (path.StartsWithSegments("/api/admin/support")
                || path.StartsWithSegments("/api/admin/locks/clear"));
    }
}
