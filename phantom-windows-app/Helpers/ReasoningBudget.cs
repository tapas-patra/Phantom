using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Services;

namespace SecureOverlay.Helpers
{
    public readonly record struct ReasoningPlan(string Effort, int MaxTokens, int ClaudeThinkingTokens, int GeminiThinkingTokens);

    public static class ReasoningBudget
    {
        private static readonly AsyncLocal<string?> QuestionType = new();

        public static string? CurrentQuestionType => QuestionType.Value;

        public static IDisposable UseQuestionType(string? questionType)
        {
            var previous = QuestionType.Value;
            QuestionType.Value = questionType;
            return new Restore(() => QuestionType.Value = previous);
        }

        public static ReasoningPlan Resolve(IEnumerable<ConversationMessage>? messages, string? questionType = null)
        {
            var type = questionType ?? CurrentQuestionType;
            var lastUser = messages?.LastOrDefault(item =>
                string.Equals(item.Role, "user", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.Content));
            var question = lastUser?.Content?.ToLowerInvariant() ?? string.Empty;
            var kind = Classify(type, question, lastUser?.HasCode == true);
            return kind switch
            {
                "high" => new("high", 8000, 5000, 4096),
                "low" => new("low", 3000, 0, 0),
                _ => new("medium", 5000, 2048, 1024)
            };
        }

        public static bool SupportsNativeThinking(string provider, string model)
        {
            if (string.Equals(provider, "OpenRouter", StringComparison.OrdinalIgnoreCase))
                return true;

            var value = model?.ToLowerInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return ContainsAny(value,
                "o1", "o3", "o4", "gpt-5",
                "sonnet-4", "opus-4", "claude-3-7", "claude-4",
                "gemini-2.5", "gemini-3",
                "magistral", "gpt-oss", "qwq", "deepseek-r", "glm-5",
                "reasoning", "thinking");
        }

        private static string Classify(string? questionType, string question, bool hasCode)
        {
            switch (questionType?.Trim().ToLowerInvariant())
            {
                case "coding":
                case "system_design":
                case "product_case":
                    return "high";
                case "behavioral":
                case "personal_factual":
                case "motivation_fit":
                case "clarification":
                case "factual_lookup":
                case "status_update":
                    return "low";
            }

            if (hasCode || ContainsAny(question, "expand", "deeper", "in detail", "step by step", "write code", "write a function", "implement", "algorithm", "complexity", "debug this", "fix this", "refactor", "leetcode", "mermaid", "system design", "design a", "architecture", "scalability", "high availability", "distributed", "load balancer", "rate limiter", "microservices"))
                return "high";
            if (question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 12
                && ContainsAny(question, "why", "how", "what about", "give an example", "clarify"))
                return "low";
            return "medium";
        }

        private static bool ContainsAny(string value, params string[] terms)
            => terms.Any(term => value.Contains(term, StringComparison.Ordinal));

        public static async Task<HttpResponseMessage> SendWithOptionalThinkingAsync(
            HttpClient client,
            Func<bool, HttpRequestMessage> buildRequest,
            bool includeThinking,
            CancellationToken cancellationToken)
        {
            var response = await client.SendAsync(buildRequest(includeThinking), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!includeThinking || (int)response.StatusCode != 400)
                return response;

            response.Dispose();
            return await client.SendAsync(buildRequest(false), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }

        private sealed class Restore : IDisposable
        {
            private readonly Action _restore;
            private bool _disposed;
            public Restore(Action restore) => _restore = restore;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _restore();
            }
        }
    }
}
