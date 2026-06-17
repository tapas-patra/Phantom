using System;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HostedServiceException : Exception
    {
        public HostedServiceException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
