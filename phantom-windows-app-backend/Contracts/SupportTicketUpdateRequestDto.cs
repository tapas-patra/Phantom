namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class SupportTicketUpdateRequestDto
{
    public string TicketId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string AdminNotes { get; set; } = string.Empty;
    public string ResolutionSummary { get; set; } = string.Empty;
}
