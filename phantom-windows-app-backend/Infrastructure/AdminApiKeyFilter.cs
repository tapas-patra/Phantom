namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    private readonly BackendOptions _options;

    public AdminApiKeyFilter(BackendOptions options)
    {
        _options = options;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!_options.HasAdminApiKey)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Admin API disabled",
                detail: "Configure PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY to enable admin endpoints.");
        }

        var request = context.HttpContext.Request;
        var providedKey = request.Headers["X-Phantom-Admin-Key"].ToString();
        if (!string.Equals(providedKey, _options.AdminApiKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
