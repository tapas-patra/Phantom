using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;
using Npgsql;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AuthService
{
    private readonly BackendOptions _options;
    private readonly AccountRepository _accounts;
    private readonly UserPasswordResetRepository _passwordResets;
    private readonly AuthSessionRepository _sessions;
    private readonly MagicLinkRepository _magicLinks;
    private readonly MagicLinkEmailService _emailService;
    private readonly PasswordHasher _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly TelemetryRepository _telemetry;
    private readonly PostgresBackendStore _store;

    public AuthService(
        BackendOptions options,
        AccountRepository accounts,
        UserPasswordResetRepository passwordResets,
        AuthSessionRepository sessions,
        MagicLinkRepository magicLinks,
        MagicLinkEmailService emailService,
        PasswordHasher passwordHasher,
        TokenService tokenService,
        TelemetryRepository telemetry,
        PostgresBackendStore store)
    {
        _options = options;
        _accounts = accounts;
        _passwordResets = passwordResets;
        _sessions = sessions;
        _magicLinks = magicLinks;
        _emailService = emailService;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _telemetry = telemetry;
        _store = store;
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
        using var connection = _store.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var existing = _sessions.FindByRefreshTokenHash(
            _tokenService.HashToken(refreshToken),
            connection,
            transaction,
            forUpdate: true) ?? throw new BackendValidationException("Refresh session not found.");

        if (!existing.IsAuthenticated || existing.RevokedAtUtc.HasValue || existing.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Refresh token is no longer valid.");
        }
        if (!string.Equals(existing.DeviceInstallId, installId, StringComparison.Ordinal)
            || !string.Equals(existing.DeviceFingerprintHash, fingerprintHash, StringComparison.Ordinal))
        {
            throw new BackendValidationException("Refresh token device mismatch.");
        }

        _sessions.RevokeBySessionId(existing.SessionId, connection, transaction);

        var accessToken = _tokenService.GenerateOpaqueToken();
        var newRefreshToken = _tokenService.GenerateOpaqueToken();
        var session = new DesktopSessionRecord
        {
            SessionId = $"session-{Guid.NewGuid():N}",
            UserId = existing.UserId,
            Email = existing.Email,
            AccessTokenHash = _tokenService.HashToken(accessToken),
            RefreshTokenHash = _tokenService.HashToken(newRefreshToken),
            AuthMethod = "refresh",
            DeviceInstallId = installId,
            DeviceFingerprintHash = fingerprintHash,
            AuthenticatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(_options.SessionTtlHours),
            IsAuthenticated = true
        };
        _sessions.Save(session, connection, transaction);
        transaction.Commit();

        return ToDto(session, accessToken, newRefreshToken);
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

    public UserPasswordResetResultDto StartPasswordReset(string email, string publicBackendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var account = _accounts.FindByEmail(normalizedEmail);
        var genericMessage = "If that account exists, a password reset link has been sent.";

        if (account == null || !account.EmailVerified)
        {
            SaveSecurityTelemetry("user_password_reset_requested_unknown", normalizedEmail);
            return new UserPasswordResetResultDto { Message = genericMessage };
        }

        var deliveryConfigurationError = _emailService.GetDeliveryConfigurationError();
        if (!string.IsNullOrWhiteSpace(deliveryConfigurationError))
        {
            throw new BackendValidationException(deliveryConfigurationError);
        }

        var token = _tokenService.GenerateOpaqueToken();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.UserPasswordResetTtlMinutes);
        var resetBaseUrl = string.IsNullOrWhiteSpace(_options.PublicWebsiteBaseUrl)
            ? publicBackendBaseUrl.TrimEnd('/')
            : _options.PublicWebsiteBaseUrl.TrimEnd('/');
        var resetUrl = $"{resetBaseUrl}/reset-password?token={Uri.EscapeDataString(token)}";
        var delivery = _emailService.SendUserPasswordReset(account.Email, resetUrl, expiresAtUtc);
        _passwordResets.Save(new UserPasswordResetTokenRecord
        {
            TokenHash = _tokenService.HashToken(token),
            UserId = account.UserId,
            Email = account.Email,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            Consumed = false,
            DeliveryStatus = delivery.Status,
            DeliveryError = delivery.Error
        });
        SaveSecurityTelemetry("user_password_reset_requested", account.Email);

        return new UserPasswordResetResultDto { Message = genericMessage };
    }

    public UserPasswordResetResultDto CompletePasswordReset(UserPasswordResetCompleteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            throw new BackendValidationException("Token is required.");
        }

        PasswordPolicy.Validate(request.NewPassword);

        var tokenRecord = _passwordResets.FindByTokenHash(_tokenService.HashToken(request.Token))
            ?? throw new BackendValidationException("Password reset token not found.");
        if (tokenRecord.Consumed)
        {
            throw new BackendValidationException("Password reset token already used.");
        }

        if (tokenRecord.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Password reset token has expired.");
        }

        var account = _accounts.FindByUserId(tokenRecord.UserId)
            ?? throw new BackendValidationException("Account not found.");

        account.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);
        _sessions.RevokeAllByUserId(account.UserId);

        tokenRecord.Consumed = true;
        tokenRecord.ConsumedAtUtc = DateTime.UtcNow;
        _passwordResets.Save(tokenRecord);
        SaveSecurityTelemetry("user_password_reset_completed", account.Email);

        return new UserPasswordResetResultDto
        {
            Message = "Account password reset complete."
        };
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

    private void SaveSecurityTelemetry(string eventName, string email)
    {
        _telemetry.Save(new TelemetryEventRecord
        {
            EventId = $"telemetry-{Guid.NewGuid():N}",
            Category = "auth_security",
            EventName = eventName,
            PayloadJson = $$"""{"email":"{{email}}"}""",
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}
