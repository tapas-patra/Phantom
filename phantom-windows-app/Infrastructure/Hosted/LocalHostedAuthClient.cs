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
            throw new HostedServiceException(
                "Local fallback authentication is disabled. Configure PHANTOM_WINDOWS_BACKEND_BASE_URL and PHANTOM_HOSTED_MODE=remote.");
        }

        public AuthMagicLinkIssuedDto RequestMagicLink(AuthMagicLinkRequestDto request)
        {
            throw new HostedServiceException(
                "Local fallback authentication is disabled. Configure PHANTOM_WINDOWS_BACKEND_BASE_URL and PHANTOM_HOSTED_MODE=remote.");
        }

        public AuthCallbackCompletionResultDto CompleteCallback(AuthCallbackCompletionRequestDto request)
        {
            throw new HostedServiceException(
                "Local fallback authentication is disabled. Configure PHANTOM_WINDOWS_BACKEND_BASE_URL and PHANTOM_HOSTED_MODE=remote.");
        }
    }
}
