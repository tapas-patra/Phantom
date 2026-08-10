using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class AdminAuthService
{
    private readonly BackendOptions _options;
    private readonly AdminAccountRepository _admins;
    private readonly AdminPasswordResetRepository _passwordResets;
    private readonly AuthSessionRepository _sessions;
    private readonly PasswordHasher _passwordHasher;
    private readonly TokenService _tokenService;
    private readonly MagicLinkEmailService _emailService;
    private readonly TelemetryRepository _telemetry;

    public AdminAuthService(
        BackendOptions options,
        AdminAccountRepository admins,
        AdminPasswordResetRepository passwordResets,
        AuthSessionRepository sessions,
        PasswordHasher passwordHasher,
        TokenService tokenService,
        MagicLinkEmailService emailService,
        TelemetryRepository telemetry)
    {
        _options = options;
        _admins = admins;
        _passwordResets = passwordResets;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _emailService = emailService;
        _telemetry = telemetry;
    }

    public AdminAuthSessionDto Login(AdminAuthLoginRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            throw new BackendValidationException("Password is required.");
        }

        var admin = _admins.FindByEmail(request.Email.Trim().ToLowerInvariant());
        if (admin == null)
        {
            if (!_admins.ExistsAny())
            {
                throw new BackendValidationException(
                    "Admin sign-in is not available until an administrator account is configured.");
            }

            throw new BackendValidationException("Invalid admin email or password.");
        }

        if (!admin.IsActive || !_passwordHasher.Verify(request.Password, admin.PasswordHash))
        {
            throw new BackendValidationException("Invalid admin email or password.");
        }

        admin.LastLoginAtUtc = DateTime.UtcNow;
        admin.UpdatedAtUtc = DateTime.UtcNow;
        _admins.Save(admin);
        SaveTelemetry("admin_login_succeeded", admin.Email);
        return CreateSession(admin, "admin:password");
    }

    public AdminAuthSessionDto Refresh(AdminAuthRefreshRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new BackendValidationException("RefreshToken is required.");
        }

        var session = _sessions.FindByRefreshTokenHash(_tokenService.HashToken(request.RefreshToken))
            ?? throw new BackendValidationException("Refresh session not found.");
        ValidateAdminSessionRecord(session);

        var admin = _admins.FindByAdminId(session.UserId)
            ?? throw new BackendValidationException("Admin session no longer exists.");
        EnsureActiveAdmin(admin);

        _sessions.RevokeBySessionId(session.SessionId);
        return CreateSession(admin, "admin:refresh");
    }

    public void Logout(string refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new BackendValidationException("RefreshToken is required.");
        }

        var session = _sessions.FindByRefreshTokenHash(_tokenService.HashToken(refreshToken))
            ?? throw new BackendValidationException("Refresh session not found.");
        ValidateAdminSessionRecord(session);
        _sessions.RevokeBySessionId(session.SessionId);
    }

    public AdminAuthSessionDto GetSession(string authorizationHeader)
    {
        var (admin, session, _) = RequireAdminSession(authorizationHeader);
        return ToDto(admin, session, string.Empty, string.Empty);
    }

    public AdminPasswordResetResultDto StartPasswordReset(string email, string publicBackendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();
        var admin = _admins.FindByEmail(normalizedEmail);
        var genericMessage = "If that admin account exists, a password reset link has been sent.";

        if (admin == null || !admin.IsActive)
        {
            SaveTelemetry("admin_password_reset_requested_unknown", normalizedEmail);
            return new AdminPasswordResetResultDto { Message = genericMessage };
        }

        var deliveryConfigurationError = _emailService.GetDeliveryConfigurationError();
        if (!string.IsNullOrWhiteSpace(deliveryConfigurationError))
        {
            throw new BackendValidationException(deliveryConfigurationError);
        }

        var token = _tokenService.GenerateOpaqueToken();
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AdminPasswordResetTtlMinutes);
        var resetBaseUrl = string.IsNullOrWhiteSpace(_options.PublicWebsiteBaseUrl)
            ? publicBackendBaseUrl.TrimEnd('/')
            : _options.PublicWebsiteBaseUrl.TrimEnd('/');
        var resetUrl = $"{resetBaseUrl}/admin/reset-password?token={Uri.EscapeDataString(token)}";
        var delivery = _emailService.SendAdminPasswordReset(admin.Email, resetUrl, expiresAtUtc);
        _passwordResets.Save(new AdminPasswordResetTokenRecord
        {
            TokenHash = _tokenService.HashToken(token),
            AdminId = admin.AdminId,
            Email = admin.Email,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            Consumed = false,
            DeliveryStatus = delivery.Status,
            DeliveryError = delivery.Error
        });
        SaveTelemetry("admin_password_reset_requested", admin.Email);

        return new AdminPasswordResetResultDto { Message = genericMessage };
    }

    public AdminPasswordResetResultDto CompletePasswordReset(AdminPasswordResetCompleteRequestDto request)
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

        var admin = _admins.FindByAdminId(tokenRecord.AdminId)
            ?? throw new BackendValidationException("Admin account not found.");
        EnsureActiveAdmin(admin);

        admin.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        admin.UpdatedAtUtc = DateTime.UtcNow;
        _admins.Save(admin);
        _sessions.RevokeAllByUserId(admin.AdminId);

        tokenRecord.Consumed = true;
        tokenRecord.ConsumedAtUtc = DateTime.UtcNow;
        _passwordResets.Save(tokenRecord);
        SaveTelemetry("admin_password_reset_completed", admin.Email);

        return new AdminPasswordResetResultDto
        {
            Message = "Admin password reset complete."
        };
    }

    public AdminAccountRecord RequireAdminFromAuthorization(string authorizationHeader)
    {
        var (admin, _, _) = RequireAdminSession(authorizationHeader);
        return admin;
    }

    private (AdminAccountRecord Admin, DesktopSessionRecord Session, string AccessToken) RequireAdminSession(string authorizationHeader)
    {
        var accessToken = ParseBearerToken(authorizationHeader);
        var session = _sessions.FindByAccessTokenHash(_tokenService.HashToken(accessToken))
            ?? throw new BackendValidationException("Admin session not found.");
        ValidateAdminSessionRecord(session);

        var admin = _admins.FindByAdminId(session.UserId)
            ?? throw new BackendValidationException("Admin account not found.");
        EnsureActiveAdmin(admin);
        return (admin, session, accessToken);
    }

    private AdminAuthSessionDto CreateSession(AdminAccountRecord admin, string authMethod)
    {
        var accessToken = _tokenService.GenerateOpaqueToken();
        var refreshToken = _tokenService.GenerateOpaqueToken();
        var session = new DesktopSessionRecord
        {
            SessionId = $"admin-session-{Guid.NewGuid():N}",
            UserId = admin.AdminId,
            Email = admin.Email,
            AccessTokenHash = _tokenService.HashToken(accessToken),
            RefreshTokenHash = _tokenService.HashToken(refreshToken),
            AuthMethod = authMethod,
            DeviceInstallId = "admin-dashboard",
            DeviceFingerprintHash = "admin-dashboard-browser",
            AuthenticatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(_options.AdminSessionTtlHours),
            IsAuthenticated = true
        };
        _sessions.Save(session);
        return ToDto(admin, session, accessToken, refreshToken);
    }

    private static AdminAuthSessionDto ToDto(
        AdminAccountRecord admin,
        DesktopSessionRecord session,
        string accessToken,
        string refreshToken)
    {
        return new AdminAuthSessionDto
        {
            AdminId = admin.AdminId,
            Email = admin.Email,
            DisplayName = admin.DisplayName,
            Role = admin.Role,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AuthMethod = session.AuthMethod,
            AuthenticatedAtUtc = session.AuthenticatedAtUtc,
            ExpiresAtUtc = session.ExpiresAtUtc,
            IsAuthenticated = session.IsAuthenticated
        };
    }

    private static string ParseBearerToken(string authorizationHeader)
    {
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Admin authorization header is required.");
        }

        var accessToken = authorizationHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new BackendValidationException("Admin access token is required.");
        }

        return accessToken;
    }

    private static void ValidateAdminSessionRecord(DesktopSessionRecord session)
    {
        if (!session.IsAuthenticated || session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Admin session is no longer valid.");
        }

        if (!session.AuthMethod.StartsWith("admin:", StringComparison.Ordinal))
        {
            throw new BackendValidationException("Session is not an admin session.");
        }
    }

    private static void EnsureActiveAdmin(AdminAccountRecord admin)
    {
        if (!admin.IsActive)
        {
            throw new BackendValidationException("Admin account is inactive.");
        }
    }

    private void SaveTelemetry(string eventName, string email)
    {
        _telemetry.Save(new TelemetryEventRecord
        {
            EventId = $"telemetry-{Guid.NewGuid():N}",
            Category = "admin_security",
            EventName = eventName,
            PayloadJson = $$"""{"email":"{{email}}"}""",
            CreatedAtUtc = DateTime.UtcNow
        });
    }
}
