namespace Phantom.WindowsApp.Backend.Contracts;

public sealed class InterviewAnswerPlanRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public bool AllowPaidSessionExtension { get; set; }
    public string Question { get; set; } = string.Empty;
    public string ActiveEntityId { get; set; } = string.Empty;
    public IReadOnlyList<DesktopAiChatMessageDto> RecentMessages { get; set; } = Array.Empty<DesktopAiChatMessageDto>();
}
