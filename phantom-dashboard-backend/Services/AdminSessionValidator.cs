namespace Phantom.Dashboard.Backend.Services;

public sealed class AdminSessionValidator
{
    private readonly AuthorityBackendClient _authority;

    public AdminSessionValidator(AuthorityBackendClient authority)
    {
        _authority = authority;
    }

    public async Task<bool> IsValidAsync(string authorizationHeader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            await _authority.SendAsync<object>(
                HttpMethod.Get,
                "/api/internal/session/admin",
                authorizationHeader,
                null,
                cancellationToken);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
