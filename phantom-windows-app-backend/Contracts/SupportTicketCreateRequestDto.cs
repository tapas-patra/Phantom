namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class SupportTicketCreateRequestDto
{
    public string Subject { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
