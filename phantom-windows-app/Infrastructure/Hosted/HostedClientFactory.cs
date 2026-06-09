using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Hosted
{
    public static class HostedClientFactory
    {
        public static HostedRuntimeOptions LoadOptions() => HostedRuntimeOptions.Load();

        public static IHostedAuthClient CreateAuthClient(DeviceProfile deviceProfile, HostedRuntimeOptions options)
        {
            return options.UseRemoteBackend
                ? new HttpHostedAuthClient(deviceProfile, options)
                : new LocalHostedAuthClient(deviceProfile);
        }

        public static IHostedAccountClient CreateAccountClient(HostedRuntimeOptions options)
        {
            return options.UseRemoteBackend
                ? new HttpHostedAccountClient(options)
                : new LocalHostedAccountClient();
        }

        public static IHostedUsageClient CreateUsageClient(HostedRuntimeOptions options)
        {
            return options.UseRemoteBackend
                ? new HttpHostedUsageClient(options)
                : new LocalHostedUsageClient();
        }

        public static IHostedTelemetryClient CreateTelemetryClient(HostedRuntimeOptions options)
        {
            return options.UseRemoteBackend
                ? new HttpHostedTelemetryClient(options)
                : new LocalHostedTelemetryClient();
        }
    }
}
