namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class CompanionRelayTicketDto
{
    public string Ticket { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string RelayUrl { get; set; } = string.Empty;
}
