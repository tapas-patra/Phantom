namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionPairingsResponseDto
{
    public IReadOnlyList<CompanionPairingDto> Pairings { get; set; } = Array.Empty<CompanionPairingDto>();
}
