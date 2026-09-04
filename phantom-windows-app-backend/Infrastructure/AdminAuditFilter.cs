using System.Reflection;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class AdminAuditFilter : IEndpointFilter
{
    private readonly AdminAuditRepository _audit;
    private readonly ILogger<AdminAuditFilter> _logger;

    public AdminAuditFilter(AdminAuditRepository audit, ILogger<AdminAuditFilter> logger)
    {
        _audit = audit;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
        {
            return await next(context);
        }

        var admin = context.HttpContext.Items[AdminApiKeyFilter.AdminContextKey] as AdminAccountRecord;
        var targetUserId = ReadStringProperty(context.Arguments, "UserId");
        var reason = FirstNonEmpty(
            ReadStringProperty(context.Arguments, "Reason"),
            ReadStringProperty(context.Arguments, "ResolutionSummary"),
            ReadStringProperty(context.Arguments, "AdminNotes"));
        object? result = null;
        Exception? failure = null;
        try
        {
            result = await next(context);
            return result;
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            try
            {
                var statusCode = result is IStatusCodeHttpResult statusResult ? statusResult.StatusCode : null;
                _audit.Record(
                    admin?.AdminId ?? "unknown",
                    admin?.Email ?? "unknown",
                    request.Method,
                    request.Path,
                    targetUserId,
                    reason,
                    request.Headers["X-Phantom-Correlation-Id"].FirstOrDefault() ?? string.Empty,
                    context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    failure == null && (!statusCode.HasValue || statusCode.Value < 400),
                    statusCode);
            }
            catch (Exception auditError)
            {
                _logger.LogError(auditError, "Failed to persist admin action audit record for {Method} {Path}.", request.Method, request.Path);
            }
        }
    }

    private static string ReadStringProperty(IList<object?> arguments, string propertyName)
    {
        foreach (var argument in arguments.Where(argument => argument != null))
        {
            var property = argument!.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property?.PropertyType == typeof(string))
            {
                return (property.GetValue(argument) as string)?.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
