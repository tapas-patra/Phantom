using Phantom.Dashboard.Backend.Services;

namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    private readonly DashboardOptions _options;
    private readonly AdminSessionValidator _validator;

    public AdminApiKeyFilter(DashboardOptions options, AdminSessionValidator validator)
    {
        _options = options;
        _validator = validator;
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
        if (await _validator.IsValidAsync(authorizationHeader, context.HttpContext.RequestAborted))
        {
            return await next(context);
        }

        return Results.Unauthorized();
    }
}
