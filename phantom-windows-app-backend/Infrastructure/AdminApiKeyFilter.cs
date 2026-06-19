using Phantom.WindowsApp.Backend.Services;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    private readonly BackendOptions _options;
    private readonly AdminAuthService _adminAuth;

    public AdminApiKeyFilter(BackendOptions options, AdminAuthService adminAuth)
    {
        _options = options;
        _adminAuth = adminAuth;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        var providedKey = request.Headers["X-Phantom-Admin-Key"].ToString();
        if (_options.HasAdminApiKey
            && string.Equals(providedKey, _options.AdminApiKey, StringComparison.Ordinal))
        {
            return await next(context);
        }

        var authorizationHeader = request.Headers.Authorization.ToString();
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

        if (!_options.HasAdminApiKey)
        {
            return Results.Unauthorized();
        }

        return Results.Unauthorized();
    }
}
