using Phantom.WindowsApp.Backend.Services;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
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
                _adminAuth.RequireAdminFromAuthorization(authorizationHeader);
                return await next(context);
            }
            catch (BackendValidationException)
            {
                return Results.Unauthorized();
            }
        }

        return Results.Unauthorized();
    }
}
