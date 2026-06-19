using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class DesktopSessionService
{
    private readonly AuthSessionRepository _sessions;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;

    public DesktopSessionService(
        AuthSessionRepository sessions,
        AccountRepository accounts,
        TokenService tokens)
    {
        _sessions = sessions;
        _accounts = accounts;
        _tokens = tokens;
    }

    public DesktopSessionRecord RequireSession(string? authorizationHeader)
    {
        var token = ParseBearerToken(authorizationHeader);
        var session = _sessions.FindByAccessTokenHash(_tokens.HashToken(token))
            ?? throw new BackendValidationException("Desktop session not found.");

        if (!session.IsAuthenticated || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Desktop session is no longer valid.");
        }

        if (session.AuthMethod.StartsWith("admin:", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("A desktop user session is required.");
        }

        return session;
    }

    public DesktopAccountRecord RequireAccount(string? authorizationHeader)
    {
        var session = RequireSession(authorizationHeader);
        return _accounts.FindByUserId(session.UserId)
            ?? throw new BackendValidationException("Account not found.");
    }

    private static string ParseBearerToken(string? authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BackendValidationException("Authorization bearer token is required.");
        }

        return token;
    }
}
