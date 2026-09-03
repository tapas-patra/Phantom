using System;

namespace SecureOverlay.Services
{
    public class ConversationMessage
    {
        public string Role { get; set; } = ""; // "system", "user", "assistant"
        public string Content { get; set; } = "";
        public string Summary { get; set; } = ""; // For context optimization
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool HasCode { get; set; } = false;
        public int EstimatedTokens { get; set; } = 0;
        public string AnswerSource { get; set; } = ""; // exact_evidence, profile_synthesis, or universal knowledge/synthesis
        public string InterviewIntent { get; set; } = ""; // Personal, General, Hybrid, or Ambiguous
    }

    public class ModelConfig
    {
        public string Name { get; set; } = "";
        public int MaxContextTokens { get; set; } = 8000;
        public int MaxResponseTokens { get; set; } = 2000;
        public int SystemPromptReserve { get; set; } = 200;
        public int ResumeSummaryReserve { get; set; } = 300;
        public int SlidingWindowSize { get; set; } = 5; // Number of conversation pairs to keep
    }
}
