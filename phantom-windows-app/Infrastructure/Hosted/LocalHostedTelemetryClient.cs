using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class LocalHostedTelemetryClient : IHostedTelemetryClient
    {
        public void Ingest(TelemetryEvent telemetryEvent)
        {
            // Local mode intentionally keeps telemetry on the device only.
        }
    }
}
