using SecureOverlay.Domain.Entities;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedTelemetryClient
    {
        void Ingest(TelemetryEvent telemetryEvent, string accessToken);
    }
}
