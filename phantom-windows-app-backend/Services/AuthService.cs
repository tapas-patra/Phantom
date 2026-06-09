using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AuthService
{
    private readonly AuthSessionRepository _sessions;

    public AuthService(AuthSessionRepository sessions)
    {
        _sessions = sessions;
    }

    public AuthSessionDto CreateSession(DesktopAccountRecord account, string authMethod, string installId, string fingerprintHash)
    {
        var session = new DesktopSessionRecord
        {
            SessionId = $"session-{Guid.NewGuid():N}",
            UserId = account.UserId,
            Email = account.Email,
            AccessToken = $"access-{Guid.NewGuid():N}",
            RefreshToken = $"refresh-{Guid.NewGuid():N}",
            AuthMethod = authMethod,
            DeviceInstallId = installId,
            DeviceFingerprintHash = fingerprintHash,
            AuthenticatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(12),
            IsAuthenticated = true
        };
        _sessions.Save(session);
        return new AuthSessionDto
        {
            UserId = session.UserId,
            Email = session.Email,
            AccessToken = session.AccessToken,
            RefreshToken = session.RefreshToken,
            AuthMethod = session.AuthMethod,
            DeviceInstallId = session.DeviceInstallId,
            DeviceFingerprintHash = session.DeviceFingerprintHash,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            IsAuthenticated = session.IsAuthenticated
        };
    }
}
