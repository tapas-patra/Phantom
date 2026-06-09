using System;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class LocalHostedAuthClient : IHostedAuthClient
    {
        private readonly DeviceProfile _deviceProfile;

        public LocalHostedAuthClient(DeviceProfile deviceProfile)
        {
            _deviceProfile = deviceProfile;
        }

        public AuthSessionDto CreateSession(AuthLoginRequestDto request)
        {
            var normalizedEmail = string.IsNullOrWhiteSpace(request.Email) ? "local-user@phantom.app" : request.Email.Trim();
            return new AuthSessionDto
            {
                UserId = normalizedEmail.ToLowerInvariant(),
                Email = normalizedEmail,
                AccessToken = $"local-access::{Guid.NewGuid():N}",
                RefreshToken = $"local-refresh::{Guid.NewGuid():N}",
                AuthMethod = request.UseMagicLink ? "magic_link" : "password",
                DeviceInstallId = _deviceProfile.InstallId,
                DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash,
                AuthenticatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddHours(12),
                IsAuthenticated = true
            };
        }

        public AuthCallbackCompletionResultDto CompleteCallback(AuthCallbackCompletionRequestDto request)
        {
            var email = ReadQueryValue(request.CallbackUri, "email") ?? "callback-user@phantom.app";
            var status = (ReadQueryValue(request.CallbackUri, "status") ?? "ready").ToLowerInvariant();
            var callbackResult = new AuthCallbackResultDto
            {
                Email = email,
                Status = status,
                PhoneVerified = status != "verify",
                DeviceInstallId = _deviceProfile.InstallId,
                DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash
            };

            return new AuthCallbackCompletionResultDto
            {
                CallbackResult = callbackResult,
                Session = new AuthSessionDto
                {
                    UserId = email.ToLowerInvariant(),
                    Email = email,
                    AccessToken = $"callback-access::{Guid.NewGuid():N}",
                    RefreshToken = $"callback-refresh::{Guid.NewGuid():N}",
                    AuthMethod = "callback",
                    DeviceInstallId = _deviceProfile.InstallId,
                    DeviceFingerprintHash = _deviceProfile.MachineFingerprintHash,
                    AuthenticatedAtUtc = DateTime.UtcNow,
                    ExpiresAtUtc = DateTime.UtcNow.AddHours(12),
                    IsAuthenticated = true
                }
            };
        }

        private static string? ReadQueryValue(string callbackUri, string key)
        {
            if (!Uri.TryCreate(callbackUri, UriKind.Absolute, out var uri))
            {
                return null;
            }

            var query = uri.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            var trimmed = query.TrimStart('?');
            var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var pair = part.Split('=', 2, StringSplitOptions.None);
                if (pair.Length == 2 && string.Equals(pair[0], key, StringComparison.OrdinalIgnoreCase))
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return null;
        }
    }
}
