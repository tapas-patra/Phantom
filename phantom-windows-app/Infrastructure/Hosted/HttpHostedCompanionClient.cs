using System;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public sealed class HttpHostedCompanionClient : HttpHostedClientBase, IHostedCompanionClient
    {
        public HttpHostedCompanionClient(HostedRuntimeOptions options)
            : base(options) { }

        public CompanionPairingStartResultDto StartPairing(CompanionPairingStartRequestDto request, string accessToken)
            => PostJson<CompanionPairingStartRequestDto, CompanionPairingStartResultDto>(
                "/api/companion/pairings/start", request, accessToken);

        public CompanionPairingCompleteResultDto CompletePairing(CompanionPairingCompleteRequestDto request, string accessToken)
            => PostJson<CompanionPairingCompleteRequestDto, CompanionPairingCompleteResultDto>(
                "/api/companion/pairings/complete", request, accessToken);

        public CompanionPairingsResponseDto ListPairings(string accessToken)
            => GetJson<CompanionPairingsResponseDto>("/api/companion/pairings", accessToken);

        public CompanionRevokeResultDto RevokePairing(string pairingId, string accessToken)
            => DeleteJson<CompanionRevokeResultDto>(
                $"/api/companion/pairings/{Uri.EscapeDataString(pairingId)}", accessToken);

        public CompanionRelayTicketDto CreateRelayTicket(CompanionRelayTicketRequestDto request, string accessToken)
            => PostJson<CompanionRelayTicketRequestDto, CompanionRelayTicketDto>(
                "/api/companion/relay-ticket", request, accessToken);
    }
}
