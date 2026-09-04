using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class RegistrationService
{
    private const string CurrentTermsVersion = "2026-08-10";
    private readonly BackendOptions _options;
    private readonly AccountRepository _accounts;
    private readonly PasswordHasher _passwordHasher;
    private readonly EmailVerificationRepository _verifications;
    private readonly MagicLinkEmailService _emailService;
    private readonly TokenService _tokenService;
    private readonly PhoneVerificationService _phoneVerification;
    private readonly RegistrationSettingsRepository _settings;

    public RegistrationService(
        BackendOptions options,
        AccountRepository accounts,
        PasswordHasher passwordHasher,
        EmailVerificationRepository verifications,
        MagicLinkEmailService emailService,
        TokenService tokenService,
        PhoneVerificationService phoneVerification,
        RegistrationSettingsRepository settings)
    {
        _options = options;
        _accounts = accounts;
        _passwordHasher = passwordHasher;
        _verifications = verifications;
        _emailService = emailService;
        _tokenService = tokenService;
        _phoneVerification = phoneVerification;
        _settings = settings;
    }

    public RegistrationSettingsDto GetSettings()
    {
        var settings = _settings.Get();
        return new RegistrationSettingsDto
        {
            PhoneVerificationRequired = settings.PhoneVerificationRequired,
            UpdatedAtUtc = settings.UpdatedAtUtc
        };
    }

    public RegistrationSettingsDto UpdateSettings(RegistrationSettingsUpdateRequestDto request)
    {
        if (request.PhoneVerificationRequired && !_options.HasOtpApiKey)
        {
            throw new BackendValidationException("Configure a production phone OTP provider before requiring phone verification.");
        }

        return new RegistrationSettingsDto
        {
            PhoneVerificationRequired = request.PhoneVerificationRequired,
            UpdatedAtUtc = _settings.Save(request.PhoneVerificationRequired)
        };
    }

    public AuthRegisterResultDto Register(AuthRegisterRequestDto request, string publicBackendBaseUrl)
    {
        if (!request.AcceptedTerms)
        {
            throw new BackendValidationException("Accept the Terms of Use and Privacy Policy before creating an account.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        PasswordPolicy.Validate(request.Password);

        var deliveryConfigurationError = _emailService.GetDeliveryConfigurationError();
        if (!string.IsNullOrWhiteSpace(deliveryConfigurationError))
        {
            throw new BackendValidationException(deliveryConfigurationError);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var phoneVerificationRequired = _settings.Get().PhoneVerificationRequired;
        var deviceFingerprintHash = request.DeviceFingerprintHash?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(deviceFingerprintHash))
        {
            throw new BackendValidationException("Device fingerprint is required for registration.");
        }
        var existing = _accounts.FindByEmail(normalizedEmail);
        if (existing != null && existing.EmailVerified)
        {
            throw new BackendValidationException("Unable to create an account with the provided details.");
        }

        PhoneVerificationChallengeRecord? verifiedPhone = null;
        var normalizedPhone = existing?.PhoneNumberE164 ?? string.Empty;
        if (phoneVerificationRequired)
        {
            normalizedPhone = PhoneVerificationService.NormalizePhone(request.PhoneNumber);
            var canReuseExistingPhoneVerification = existing != null
                && !existing.EmailVerified
                && existing.PhoneVerified
                && string.Equals(existing.PhoneNumberE164, normalizedPhone, StringComparison.Ordinal)
                && string.Equals(existing.RegistrationDeviceFingerprintHash, deviceFingerprintHash, StringComparison.Ordinal);

            if (!canReuseExistingPhoneVerification)
            {
                verifiedPhone = _phoneVerification.ConsumeVerifiedToken(
                    request.PhoneVerificationToken,
                    request.PhoneNumber,
                    deviceFingerprintHash);
            }
        }

        var now = DateTime.UtcNow;
        var account = existing ?? new DesktopAccountRecord
        {
            UserId = $"user-{Guid.NewGuid():N}",
            Email = normalizedEmail,
            AccessTier = "free",
            PhoneNumberE164 = verifiedPhone?.PhoneNumberE164 ?? normalizedPhone,
            PhoneVerified = !phoneVerificationRequired || verifiedPhone != null,
            PhoneVerifiedAtUtc = verifiedPhone?.VerifiedAtUtc,
            RegistrationDeviceFingerprintHash = deviceFingerprintHash,
            ProAvailableCredits = 0m,
            PremiumAvailableCredits = 0.5m,
            PremiumNegativeCredits = 0m,
            LeaseExpiresAtUtc = now.AddHours(_options.DefaultLeaseHours),
            OfflineModeEnabled = false,
            CanUseDesktopPowerFeatures = false,
            CreatedAtUtc = now
        };

        account.Email = normalizedEmail;
        if (phoneVerificationRequired)
        {
            account.PhoneNumberE164 = verifiedPhone?.PhoneNumberE164 ?? normalizedPhone;
            account.PhoneVerified = true;
            account.PhoneVerifiedAtUtc = verifiedPhone?.VerifiedAtUtc ?? account.PhoneVerifiedAtUtc ?? now;
        }
        else
        {
            // PhoneVerified also acts as the persisted signup-gate result, grandfathering this account if the setting changes later.
            account.PhoneVerified = true;
        }
        account.RegistrationDeviceFingerprintHash = deviceFingerprintHash;
        account.TermsAcceptedAtUtc = now;
        account.TermsVersion = CurrentTermsVersion;
        if (AccessModeResolver.IsFree(account))
        {
            account.PremiumAvailableCredits = Math.Max(account.PremiumAvailableCredits, 0.5m);
        }
        account.EmailVerified = false;
        account.EmailVerifiedAtUtc = null;
        account.PasswordHash = _passwordHasher.Hash(request.Password);
        account.AccessTier = AccessModeResolver.GetEffectiveAccessTier(account);
        account.LastValidatedAtUtc = now;
        account.UpdatedAtUtc = now;

        _accounts.Save(account);

        var verification = IssueVerification(account, publicBackendBaseUrl);
        return new AuthRegisterResultDto
        {
            UserId = account.UserId,
            Email = account.Email,
            EmailVerificationRequired = true,
            DeliveryStatus = verification.DeliveryStatus,
            DeliveryError = verification.DeliveryError
        };
    }

    public AuthEmailVerificationResultDto ResendVerification(AuthEmailVerificationRequestDto request, string publicBackendBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            throw new BackendValidationException("Email is required.");
        }

        var account = _accounts.FindByEmail(request.Email.Trim().ToLowerInvariant())
            ?? throw new BackendValidationException("If the account exists, a verification email will be sent.");
        if (account.EmailVerified)
        {
            return new AuthEmailVerificationResultDto
            {
                Email = string.Empty,
                Verified = true,
                Message = "If the account exists, a verification email will be sent."
            };
        }

        var deliveryConfigurationError = _emailService.GetDeliveryConfigurationError();
        if (!string.IsNullOrWhiteSpace(deliveryConfigurationError))
        {
            throw new BackendValidationException(deliveryConfigurationError);
        }

        var verification = IssueVerification(account, publicBackendBaseUrl);
        return new AuthEmailVerificationResultDto
        {
            Email = string.Empty,
            Verified = false,
            Message = "If the account exists, a verification email will be sent."
        };
    }

    public AuthEmailVerificationResultDto CompleteVerification(string token)
    {
        var verification = _verifications.FindByTokenHash(_tokenService.HashToken(token))
            ?? throw new BackendValidationException("Verification token not found.");
        if (verification.Consumed)
        {
            throw new BackendValidationException("Verification token already used.");
        }

        if (verification.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("Verification token has expired.");
        }

        var account = _accounts.FindByUserId(verification.UserId)
            ?? throw new BackendValidationException("Account not found.");
        account.EmailVerified = true;
        account.EmailVerifiedAtUtc = DateTime.UtcNow;
        account.LastValidatedAtUtc = DateTime.UtcNow;
        account.UpdatedAtUtc = DateTime.UtcNow;
        _accounts.Save(account);

        verification.Consumed = true;
        verification.ConsumedAtUtc = DateTime.UtcNow;
        _verifications.Save(verification);

        return new AuthEmailVerificationResultDto
        {
            Email = account.Email,
            Verified = true,
            Message = "Email verified successfully."
        };
    }

    private EmailVerificationTokenRecord IssueVerification(DesktopAccountRecord account, string publicBackendBaseUrl)
    {
        var token = _tokenService.GenerateOpaqueToken();
        var expiresAtUtc = DateTime.UtcNow.AddHours(_options.EmailVerificationTtlHours);
        var verificationUrl = $"{publicBackendBaseUrl.TrimEnd('/')}/email/verify?token={Uri.EscapeDataString(token)}";
        var delivery = _emailService.SendEmailVerification(account.Email, verificationUrl, expiresAtUtc);

        var record = new EmailVerificationTokenRecord
        {
            TokenHash = _tokenService.HashToken(token),
            UserId = account.UserId,
            Email = account.Email,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
            Consumed = false,
            DeliveryStatus = delivery.Status,
            DeliveryError = delivery.Error
        };
        _verifications.Save(record);
        return record;
    }
}
