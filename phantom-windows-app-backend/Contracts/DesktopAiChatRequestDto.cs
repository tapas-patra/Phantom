namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class DesktopAiChatRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AllowPaidSessionExtension { get; set; }
    public string? ImageBase64 { get; set; }
    public IReadOnlyList<DesktopAiChatMessageDto> Messages { get; set; } = Array.Empty<DesktopAiChatMessageDto>();
}

public sealed class DesktopAiChatMessageDto
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
