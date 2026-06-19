using Microsoft.AspNetCore.Http;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public static class RequestTokenResolver
{
    public static string GetAuthorizationHeader(HttpRequest request, string? cookieToken = null)
    {
        var header = request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(header))
        {
            return header;
        }

        return string.IsNullOrWhiteSpace(cookieToken)
            ? string.Empty
            : $"Bearer {cookieToken}";
    }
}
