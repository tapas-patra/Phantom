using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Infrastructure.Hosted
{
    public interface IHostedCompanionClient
    {
        CompanionPairingStartResultDto StartPairing(CompanionPairingStartRequestDto request, string accessToken);
        CompanionPairingCompleteResultDto CompletePairing(CompanionPairingCompleteRequestDto request, string accessToken);
        CompanionPairingsResponseDto ListPairings(string accessToken);
        CompanionRevokeResultDto RevokePairing(string pairingId, string accessToken);
        CompanionRelayTicketDto CreateRelayTicket(CompanionRelayTicketRequestDto request, string accessToken);
    }
}
