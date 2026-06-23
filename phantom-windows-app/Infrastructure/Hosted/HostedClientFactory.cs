using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Hosted
{
    public static class HostedClientFactory
    {
        public static HostedRuntimeOptions LoadOptions() => HostedRuntimeOptions.Load();

        public static IHostedAuthClient CreateAuthClient(DeviceProfile deviceProfile, HostedRuntimeOptions options)
        {
            return new HttpHostedAuthClient(deviceProfile, options);
        }

        public static IHostedAccountClient CreateAccountClient(HostedRuntimeOptions options)
        {
            return new HttpHostedAccountClient(options);
        }

        public static IHostedUsageClient CreateUsageClient(HostedRuntimeOptions options)
        {
            return new HttpHostedUsageClient(options);
        }

        public static IHostedTelemetryClient CreateTelemetryClient(HostedRuntimeOptions options)
        {
            return new HttpHostedTelemetryClient(options);
        }

        public static IHostedLockClient CreateLockClient(HostedRuntimeOptions options)
        {
            return new HttpHostedLockClient(options);
        }
    }
}
