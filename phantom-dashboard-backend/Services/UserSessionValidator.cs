namespace Phantom.Dashboard.Backend.Services;

public sealed class UserSessionValidator
{
    private readonly AuthorityBackendClient _authority;

    public UserSessionValidator(AuthorityBackendClient authority)
    {
        _authority = authority;
    }

    public async Task<DashboardUserSession> RequireUserSessionAsync(string authorizationHeader, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Authorization bearer token is required.");
        }

        var payload = await _authority.SendAsync<DashboardUserSession>(
            HttpMethod.Get,
            "/api/internal/session/user",
            authorizationHeader,
            null,
            cancellationToken);
        return payload ?? throw new InvalidOperationException("User session validation returned no payload.");
    }
}

public sealed record DashboardUserSession(
    string UserId,
    string Email,
    string DeviceInstallId,
    string DeviceFingerprintHash);
