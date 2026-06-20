namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class InternalApiKeyFilter : IEndpointFilter
{
    private readonly BackendOptions _options;

    public InternalApiKeyFilter(BackendOptions options)
    {
        _options = options;
    }

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!_options.HasInternalApiKey)
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        var providedKey = context.HttpContext.Request.Headers["X-Phantom-Internal-Key"].ToString();
        if (!string.Equals(providedKey, _options.InternalApiKey, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<object?>(Results.Unauthorized());
        }

        return next(context);
    }
}
