using System.Collections.Generic;

namespace SecureOverlay.Infrastructure.Hosted.Contracts
{
    public sealed class InterviewAnswerPlanRequestDto
    {
        public string RequestId { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool AllowPaidSessionExtension { get; set; }
        public string Question { get; set; } = string.Empty;
        public string ActiveEntityId { get; set; } = string.Empty;
        public IReadOnlyList<DesktopAiChatMessageDto> RecentMessages { get; set; } = new List<DesktopAiChatMessageDto>();
    }

    public sealed class DesktopAiChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
