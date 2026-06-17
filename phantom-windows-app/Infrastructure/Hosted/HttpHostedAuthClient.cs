using System.Reflection;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedAuthClient : HttpHostedClientBase, IHostedAuthClient
    {
        private readonly DeviceProfile _deviceProfile;

        public HttpHostedAuthClient(DeviceProfile deviceProfile, HostedRuntimeOptions options)
            : base(options)
        {
            _deviceProfile = deviceProfile;
        }

        public AuthSessionDto CreateSession(AuthLoginRequestDto request)
        {
            request.AppVersion = string.IsNullOrWhiteSpace(request.AppVersion)
                ? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown"
                : request.AppVersion;
            request.InstallId = string.IsNullOrWhiteSpace(request.InstallId) ? _deviceProfile.InstallId : request.InstallId;
            request.DeviceLabel = string.IsNullOrWhiteSpace(request.DeviceLabel) ? _deviceProfile.DeviceLabel : request.DeviceLabel;
            request.DeviceFingerprintHash = string.IsNullOrWhiteSpace(request.DeviceFingerprintHash) ? _deviceProfile.MachineFingerprintHash : request.DeviceFingerprintHash;
            request.SecretFingerprintHint = string.IsNullOrWhiteSpace(request.SecretFingerprintHint) ? _deviceProfile.SecretFingerprintHint : request.SecretFingerprintHint;

            return PostJson<AuthLoginRequestDto, AuthSessionDto>("/api/desktop/auth/login", request);
        }

        public AuthSessionDto RefreshSession(AuthRefreshRequestDto request)
        {
            request.InstallId = string.IsNullOrWhiteSpace(request.InstallId) ? _deviceProfile.InstallId : request.InstallId;
            request.DeviceFingerprintHash = string.IsNullOrWhiteSpace(request.DeviceFingerprintHash) ? _deviceProfile.MachineFingerprintHash : request.DeviceFingerprintHash;

            return PostJson<AuthRefreshRequestDto, AuthSessionDto>("/api/desktop/auth/refresh", request);
        }
    }
}
