using Phantom.WindowsApp.Backend.Contracts;

namespace Phantom.WindowsApp.Backend.Services;

public readonly record struct ReasoningPlan(
    string Effort,
    int AnswerTokens,
    int ReserveTokens,
    int MinOutputTokens,
    int MaxOutputTokens,
    int ClaudeThinkingTokens,
    int GeminiThinkingTokens,
    TimeSpan FirstTokenDeadline)
{
    public int OutputTokens => Math.Clamp(AnswerTokens + ReserveTokens, MinOutputTokens, MaxOutputTokens);
}

public static class ReasoningPlanner
{
    public static ReasoningPlan For(IReadOnlyList<DesktopAiChatMessageDto> messages, string? questionType = null)
        => FromKind(Classify(messages, questionType));

    public static bool SupportsNativeThinking(string provider, string model)
    {
        if (ManagedAiCatalog.IsOpenRouter(provider))
        {
            return true;
        }

        var value = model?.ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ContainsAny(value,
            "o1", "o3", "o4", "gpt-5",
            "sonnet-4", "opus-4", "claude-3-7", "claude-4",
            "gemini-2.5", "gemini-3",
            "magistral", "gpt-oss", "qwq", "deepseek-r", "glm-5",
            "reasoning", "thinking");
    }

    public static LiveQuestionKind Classify(IReadOnlyList<DesktopAiChatMessageDto> messages, string? questionType)
    {
        var typed = ClassifyQuestionType(questionType);
        if (typed != LiveQuestionKind.Default)
        {
            return typed;
        }

        var question = messages.LastOrDefault(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))?.Content
            ?.ToLowerInvariant() ?? string.Empty;
        if (ContainsAny(question, "expand", "deeper", "in detail", "step by step")) return LiveQuestionKind.Expand;
        if (ContainsAny(question, "write code", "write a function", "implement", "algorithm", "complexity", "debug this", "fix this", "refactor", "leetcode", "mermaid"))
            return LiveQuestionKind.Coding;
        if (ContainsAny(question, "system design", "design a", "architecture", "scalability", "high availability", "distributed", "load balancer", "rate limiter", "microservices"))
            return LiveQuestionKind.SystemDesign;
        if (ContainsAny(question, "my project", "your project", "project called", "project named")) return LiveQuestionKind.Project;
        if (question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12
            && ContainsAny(question, "why", "how", "what about", "give an example", "clarify"))
            return LiveQuestionKind.Brief;
        return LiveQuestionKind.Default;
    }

    public static LiveQuestionKind ClassifyQuestionType(string? questionType)
    {
        var value = questionType?.Trim().ToLowerInvariant() ?? string.Empty;
        return value switch
        {
            "coding" or "system_design" or "product_case" => LiveQuestionKind.Coding,
            "behavioral" or "personal_factual" or "motivation_fit" or "clarification"
                or "factual_lookup" or "status_update" => LiveQuestionKind.Brief,
            "technical" or "situational" or "unknown" or "decision_support" or "objection_response"
                or "risk_tradeoff" or "brainstorm" or "action_capture" => LiveQuestionKind.Default,
            _ => LiveQuestionKind.Default
        };
    }

    private static ReasoningPlan FromKind(LiveQuestionKind kind) => kind switch
    {
        LiveQuestionKind.Brief => new("low", 320, 1024, 2048, 3072, 0, 0, TimeSpan.FromSeconds(25)),
        LiveQuestionKind.Coding or LiveQuestionKind.SystemDesign or LiveQuestionKind.Expand
            => new("high", 1600, 4096, 4096, 8192, 5000, 4096, TimeSpan.FromSeconds(90)),
        LiveQuestionKind.Project => new("medium", 800, 2048, 3072, 6144, 2048, 1024, TimeSpan.FromSeconds(45)),
        _ => new("medium", 700, 2048, 3072, 6144, 2048, 1024, TimeSpan.FromSeconds(45))
    };

    private static bool ContainsAny(string value, params string[] terms)
        => terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}

public enum LiveQuestionKind
{
    Brief,
    Default,
    Project,
    Coding,
    SystemDesign,
    Expand
}
