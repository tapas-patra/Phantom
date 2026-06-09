using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AuthService
{
    private readonly AuthSessionRepository _sessions;
    private readonly MagicLinkRepository _magicLinks;

    public AuthService(AuthSessionRepository sessions, MagicLinkRepository magicLinks)
    {
        _sessions = sessions;
        _magicLinks = magicLinks;
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

    public AuthMagicLinkIssuedDto IssueMagicLink(AuthMagicLinkRequestDto request, string publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var token = Guid.NewGuid().ToString("N");
        _magicLinks.Save(new MagicLinkRecord
        {
            Token = token,
            Email = normalizedEmail,
            InstallId = request.InstallId,
            DeviceFingerprintHash = request.DeviceFingerprintHash,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            CreatedAtUtc = DateTime.UtcNow,
            Consumed = false
        });

        var callbackUri = $"phantom://auth/callback?token={Uri.EscapeDataString(token)}";
        return new AuthMagicLinkIssuedDto
        {
            Email = normalizedEmail,
            MagicLinkUrl = $"{publicBaseUrl.TrimEnd('/')}/magic-link/consume?token={Uri.EscapeDataString(token)}",
            CallbackUri = callbackUri,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15)
        };
    }
}
