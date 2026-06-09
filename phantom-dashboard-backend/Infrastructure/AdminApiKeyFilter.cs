namespace Phantom.Dashboard.Backend.Infrastructure;

public sealed class AdminApiKeyFilter : IEndpointFilter
{
    private readonly DashboardOptions _options;

    public AdminApiKeyFilter(DashboardOptions options)
    {
        _options = options;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!_options.HasAdminApiKey)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Admin dashboard disabled",
                detail: "Configure PHANTOM_DASHBOARD_ADMIN_API_KEY to enable admin endpoints.");
        }

        var providedKey = context.HttpContext.Request.Headers["X-Phantom-Admin-Key"].ToString();
        if (!string.Equals(providedKey, _options.AdminApiKey, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
