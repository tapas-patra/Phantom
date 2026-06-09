using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AuthService
{
    private readonly BackendOptions _options;
    private readonly AuthSessionRepository _sessions;
    private readonly MagicLinkRepository _magicLinks;
    private readonly MagicLinkEmailService _emailService;
    private readonly TokenService _tokenService;
    private readonly TelemetryRepository _telemetry;

    public AuthService(
        BackendOptions options,
        AuthSessionRepository sessions,
        MagicLinkRepository magicLinks,
        MagicLinkEmailService emailService,
        TokenService tokenService,
        TelemetryRepository telemetry)
    {
        _options = options;
        _sessions = sessions;
        _magicLinks = magicLinks;
        _emailService = emailService;
        _tokenService = tokenService;
        _telemetry = telemetry;
    }

    public AuthSessionDto CreateSession(DesktopAccountRecord account, string authMethod, string installId, string fingerprintHash)
    {
        var accessToken = _tokenService.GenerateOpaqueToken();
        var refreshToken = _tokenService.GenerateOpaqueToken();

        var session = new DesktopSessionRecord
        {
            SessionId = $"session-{Guid.NewGuid():N}",
            UserId = account.UserId,
            Email = account.Email,
            AccessTokenHash = _tokenService.HashToken(accessToken),
            RefreshTokenHash = _tokenService.HashToken(refreshToken),
            AuthMethod = authMethod,
            DeviceInstallId = installId,
            DeviceFingerprintHash = fingerprintHash,
            AuthenticatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(_options.SessionTtlHours),
            IsAuthenticated = true
        };
        _sessions.Save(session);

        return ToDto(session, accessToken, refreshToken);
    }

    public AuthSessionDto RefreshSession(string refreshToken, string installId, string fingerprintHash)
    {
        var existing = _sessions.FindByRefreshTokenHash(_tokenService.HashToken(refreshToken))
            ?? throw new BackendValidationException("Refresh session not found.");

        if (!existing.IsAuthenticated || existing.RevokedAtUtc.HasValue || existing.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Refresh token is no longer valid.");
        }
        if (!string.Equals(existing.DeviceInstallId, installId, StringComparison.Ordinal)
            || !string.Equals(existing.DeviceFingerprintHash, fingerprintHash, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Refresh token device mismatch.");
        }

        _sessions.RevokeBySessionId(existing.SessionId);
        return CreateSession(new DesktopAccountRecord
        {
            UserId = existing.UserId,
            Email = existing.Email
        }, "refresh", installId, fingerprintHash);
    }

    public void RevokeSession(string refreshToken)
    {
        var existing = _sessions.FindByRefreshTokenHash(_tokenService.HashToken(refreshToken))
            ?? throw new BackendValidationException("Refresh session not found.");
        _sessions.RevokeBySessionId(existing.SessionId);
    }

    public AuthMagicLinkIssuedDto IssueMagicLink(AuthMagicLinkRequestDto request, string publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var token = _tokenService.GenerateOpaqueToken();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.MagicLinkTtlMinutes);
        var magicLinkUrl = $"{publicBaseUrl.TrimEnd('/')}/magic-link/consume?token={Uri.EscapeDataString(token)}";

        var delivery = _emailService.Send(normalizedEmail, magicLinkUrl, expiresAtUtc);
        _magicLinks.Save(new MagicLinkRecord
        {
            TokenHash = _tokenService.HashToken(token),
            Email = normalizedEmail,
            InstallId = request.InstallId,
            DeviceFingerprintHash = request.DeviceFingerprintHash,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            Consumed = false,
            DeliveryStatus = delivery.Status,
            DeliveryError = delivery.Error
        });

        _telemetry.Save(new TelemetryEventRecord
        {
            EventId = $"telemetry-{Guid.NewGuid():N}",
            Category = "auth",
            EventName = "magic_link_issued",
            PayloadJson = $$"""{"email":"{{normalizedEmail}}","delivery_status":"{{delivery.Status}}"}""",
            CreatedAtUtc = DateTime.UtcNow
        });

        var callbackUri = $"phantom://auth/callback?token={Uri.EscapeDataString(token)}";
        return new AuthMagicLinkIssuedDto
        {
            Email = normalizedEmail,
            MagicLinkUrl = magicLinkUrl,
            CallbackUri = callbackUri,
            ExpiresAtUtc = expiresAtUtc
        };
    }

    public MagicLinkRecord RequireMagicLink(string token)
    {
        return _magicLinks.FindByTokenHash(_tokenService.HashToken(token))
            ?? throw new BackendValidationException("Magic link token not found.");
    }

    private static AuthSessionDto ToDto(DesktopSessionRecord session, string accessToken, string refreshToken)
    {
        return new AuthSessionDto
        {
            UserId = session.UserId,
            Email = session.Email,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AuthMethod = session.AuthMethod,
            DeviceInstallId = session.DeviceInstallId,
            DeviceFingerprintHash = session.DeviceFingerprintHash,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            IsAuthenticated = session.IsAuthenticated
        };
    }
}
