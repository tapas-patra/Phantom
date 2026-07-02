namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class AdminManagedAiTestRequestDto
{
    public string ProviderId { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? ImageBase64 { get; set; }
    public IReadOnlyList<DesktopAiChatMessageDto> Messages { get; set; } = Array.Empty<DesktopAiChatMessageDto>();
}
