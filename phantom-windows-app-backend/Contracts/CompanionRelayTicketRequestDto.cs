namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionRelayTicketRequestDto
{
    public string PairingId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}
