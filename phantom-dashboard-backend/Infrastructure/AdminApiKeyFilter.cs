using Phantom.Dashboard.Backend.Services;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    private readonly AdminSessionValidator _validator;
    private readonly BrowserSessionCookieService _cookies;

    public AdminApiKeyFilter(AdminSessionValidator validator, BrowserSessionCookieService cookies)
    {
        _validator = validator;
        _cookies = cookies;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authorizationHeader = _cookies.GetAdminAuthorizationHeader(context.HttpContext.Request);
        if (await _validator.IsValidAsync(authorizationHeader, context.HttpContext.RequestAborted))
        {
            return await next(context);
        }

        return Results.Unauthorized();
    }
}
