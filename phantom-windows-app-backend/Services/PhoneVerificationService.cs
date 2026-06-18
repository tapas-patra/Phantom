using System.Text.RegularExpressions;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class PhoneVerificationService
{
    private static readonly Regex DigitsOnly = new("[^0-9]", RegexOptions.Compiled);

    private readonly BackendOptions _options;
    private readonly PhoneVerificationRepository _phoneVerifications;
    private readonly AccountRepository _accounts;
    private readonly TokenService _tokens;
    private readonly TwoFactorOtpClient _otpClient;

    public PhoneVerificationService(
        BackendOptions options,
        PhoneVerificationRepository phoneVerifications,
        AccountRepository accounts,
        TokenService tokens,
        TwoFactorOtpClient otpClient)
    {
        _options = options;
        _phoneVerifications = phoneVerifications;
        _accounts = accounts;
        _tokens = tokens;
        _otpClient = otpClient;
    }

    public async Task<PhoneVerificationStartResultDto> StartAsync(PhoneVerificationStartRequestDto request, CancellationToken cancellationToken)
    {
        if (!_options.HasOtpApiKey)
        {
            throw new BackendValidationException("Phone OTP is not configured on the backend.");
        }

        var phoneNumberE164 = NormalizePhone(request.PhoneNumber);
        if (string.IsNullOrWhiteSpace(request.DeviceFingerprintHash))
        {
            throw new BackendValidationException("Device fingerprint is required for trial protection.");
        }

        var existingPhoneAccount = _accounts.FindByPhoneNumber(phoneNumberE164);
        if (existingPhoneAccount != null)
        {
            throw new BackendValidationException("This phone number has already been used for registration.");
        }

        var existingFingerprintAccount = _accounts.FindByRegistrationFingerprint(request.DeviceFingerprintHash.Trim());
        if (existingFingerprintAccount != null)
        {
            throw new BackendValidationException("This device has already claimed the launch trial.");
        }

        if (_phoneVerifications.CountRecentByPhone(phoneNumberE164, TimeSpan.FromHours(1)) >= 5
            || _phoneVerifications.CountRecentByFingerprint(request.DeviceFingerprintHash.Trim(), TimeSpan.FromHours(1)) >= 5)
        {
            throw new BackendValidationException("Too many OTP requests. Please wait 30 minutes before trying again.");
        }

        var providerResult = await _otpClient.SendAsync(phoneNumberE164, cancellationToken);
        var challenge = new PhoneVerificationChallengeRecord
        {
            ChallengeId = $"phone-verify-{Guid.NewGuid():N}",
            PhoneNumberE164 = phoneNumberE164,
            PhoneNumberMasked = MaskPhone(phoneNumberE164),
            DeviceFingerprintHash = request.DeviceFingerprintHash.Trim(),
            InstallId = request.InstallId?.Trim() ?? string.Empty,
            EmailHint = request.EmailHint?.Trim().ToLowerInvariant() ?? string.Empty,
            ProviderName = _options.OtpProviderName,
            ProviderSessionId = providerResult.SessionId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = providerResult.ExpiresAtUtc,
            SendAttemptCount = 1,
            VerifyAttemptCount = 0,
            Status = "pending",
            VerificationTokenHash = string.Empty,
            FailureReason = string.Empty
        };
        _phoneVerifications.Save(challenge);

        return new PhoneVerificationStartResultDto
        {
            ChallengeId = challenge.ChallengeId,
            MaskedPhoneNumber = challenge.PhoneNumberMasked,
            ExpiresAtUtc = challenge.ExpiresAtUtc,
            RetryAfterSeconds = 30,
            Status = challenge.Status
        };
    }

    public async Task<PhoneVerificationConfirmResultDto> ConfirmAsync(PhoneVerificationConfirmRequestDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ChallengeId) || string.IsNullOrWhiteSpace(request.OtpCode))
        {
            throw new BackendValidationException("ChallengeId and OtpCode are required.");
        }

        var challenge = _phoneVerifications.FindById(request.ChallengeId.Trim())
            ?? throw new BackendValidationException("Phone verification challenge not found.");

        if (challenge.ConsumedAtUtc.HasValue)
        {
            throw new BackendValidationException("This phone verification challenge has already been used.");
        }

        if (challenge.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("This phone verification challenge has expired.");
        }

        if (challenge.VerifyAttemptCount >= 5)
        {
            throw new BackendValidationException("Too many invalid OTP attempts. Request a new code.");
        }

        try
        {
            await _otpClient.VerifyAsync(challenge.ProviderSessionId, request.OtpCode.Trim(), cancellationToken);
        }
        catch (Exception ex)
        {
            challenge.VerifyAttemptCount += 1;
            challenge.FailureReason = ex.Message;
            if (challenge.VerifyAttemptCount >= 5)
            {
                challenge.Status = "locked";
                challenge.CooldownUntilUtc = DateTime.UtcNow.AddMinutes(30);
            }

            _phoneVerifications.Save(challenge);
            throw new BackendValidationException("Invalid OTP.");
        }

        var verificationToken = _tokens.GenerateOpaqueToken();
        challenge.VerifiedAtUtc = DateTime.UtcNow;
        challenge.Status = "verified";
        challenge.VerificationTokenHash = _tokens.HashToken(verificationToken);
        _phoneVerifications.Save(challenge);

        return new PhoneVerificationConfirmResultDto
        {
            VerificationToken = verificationToken,
            MaskedPhoneNumber = challenge.PhoneNumberMasked,
            VerifiedAtUtc = challenge.VerifiedAtUtc.Value
        };
    }

    public PhoneVerificationChallengeRecord ConsumeVerifiedToken(string phoneVerificationToken, string phoneNumber, string deviceFingerprintHash)
    {
        if (string.IsNullOrWhiteSpace(phoneVerificationToken))
        {
            throw new BackendValidationException("Phone verification is required before registration.");
        }

        var challenge = _phoneVerifications.FindByVerificationTokenHash(_tokens.HashToken(phoneVerificationToken.Trim()))
            ?? throw new BackendValidationException("Phone verification token not found.");

        if (challenge.VerifiedAtUtc == null || !string.Equals(challenge.Status, "verified", StringComparison.OrdinalIgnoreCase))
        {
            throw new BackendValidationException("Phone verification is incomplete.");
        }

        if (challenge.ConsumedAtUtc.HasValue)
        {
            throw new BackendValidationException("Phone verification token has already been used.");
        }

        if (!string.Equals(challenge.PhoneNumberE164, NormalizePhone(phoneNumber), StringComparison.Ordinal))
        {
            throw new BackendValidationException("Phone verification does not match the provided phone number.");
        }

        if (!string.Equals(challenge.DeviceFingerprintHash, deviceFingerprintHash.Trim(), StringComparison.Ordinal))
        {
            throw new BackendValidationException("Phone verification does not match this device fingerprint.");
        }

        challenge.ConsumedAtUtc = DateTime.UtcNow;
        challenge.Status = "consumed";
        _phoneVerifications.Save(challenge);
        return challenge;
    }

    public static string NormalizePhone(string phoneNumber)
    {
        var digits = DigitsOnly.Replace(phoneNumber ?? string.Empty, string.Empty);
        if (digits.Length == 10)
        {
            return $"+91{digits}";
        }

        if (digits.Length == 12 && digits.StartsWith("91", StringComparison.Ordinal))
        {
            return $"+{digits}";
        }

        throw new BackendValidationException("Enter a valid Indian mobile number.");
    }

    private static string MaskPhone(string phoneNumberE164)
    {
        if (phoneNumberE164.Length < 6)
        {
            return phoneNumberE164;
        }

        return $"{phoneNumberE164[..3]}*****{phoneNumberE164[^2..]}";
    }
}
