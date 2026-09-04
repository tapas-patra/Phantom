using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Domain;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Helpers;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    /// <summary>Native state plus provider-neutral adaptive live-turn orchestration.</summary>
    public sealed class ConversationManager
    {
        private readonly Dictionary<CopilotMode, List<ConversationMessage>> _histories = new()
        {
            [CopilotMode.Interview] = new(), [CopilotMode.Briefing] = new()
        };
        private readonly Dictionary<CopilotMode, IReadOnlyList<RetrievedContextSnippet>> _activeEvidence = new()
        {
            [CopilotMode.Interview] = Array.Empty<RetrievedContextSnippet>(),
            [CopilotMode.Briefing] = Array.Empty<RetrievedContextSnippet>()
        };
        private readonly Func<string, IReadOnlyList<string>?, CancellationToken, Task<IReadOnlyList<RetrievedContextSnippet>>>? _knowledgeRetriever;
        private readonly Func<CancellationToken, Task<HostedKnowledgeBaseSummaryDto>>? _knowledgeBaseLoader;
        private readonly Func<bool>? _knowledgeRetrievalEnabled;
        private IAIService _aiService;
        private ModelConfig _modelConfig;
        private APIRotationManager? _rotationManager;
        private Func<IAIService?>? _retryServiceFactory;
        private string _currentProvider;
        private string _systemPrompt;
        private string _resumeText = string.Empty;
        private string _resumeSummary = string.Empty;
        private string _jobDescriptionText = string.Empty;
        private string _jobDescriptionSummary = string.Empty;
        private Task<bool>? _resumeSummaryTask;
        private Task<bool>? _jobDescriptionSummaryTask;
        private HostedKnowledgeBaseSummaryDto? _knowledgeBaseSummary;
        private Task<HostedKnowledgeBaseSummaryDto?>? _knowledgeBaseSummaryTask;
        private CopilotMode _copilotMode = CopilotMode.Interview;
        private InterviewDeliveryStyle _deliveryStyle = InterviewDeliveryStyle.Standard;

        public IReadOnlyList<ClarificationOption> PendingClarificationOptions { get; private set; } = Array.Empty<ClarificationOption>();
        public LiveTurnDecision? LastDecision { get; private set; }
        public int LastModelCallCount { get; private set; }
        public event EventHandler<string>? APISwitchNotification;
        public event EventHandler<string>? StageChanged;
        public event Action<LiveTurnDecision, int>? DecisionParsed;

        public ConversationManager(
            IAIService aiService, string systemPrompt, ModelConfig modelConfig,
            APIRotationManager? rotationManager = null,
            Func<string, IReadOnlyList<string>?, CancellationToken, Task<IReadOnlyList<RetrievedContextSnippet>>>? knowledgeRetriever = null,
            Func<CancellationToken, Task<HostedKnowledgeBaseSummaryDto>>? knowledgeBaseLoader = null,
            Func<bool>? knowledgeRetrievalEnabled = null)
        {
            _aiService = aiService;
            _systemPrompt = systemPrompt;
            _modelConfig = modelConfig;
            _rotationManager = rotationManager;
            _currentProvider = aiService.GetProviderName();
            _knowledgeRetriever = knowledgeRetriever;
            _knowledgeBaseLoader = knowledgeBaseLoader;
            _knowledgeRetrievalEnabled = knowledgeRetrievalEnabled;
        }

        private List<ConversationMessage> CurrentHistory => _histories[_copilotMode];

        public void UpdateAIService(IAIService service) { _aiService = service; _currentProvider = service.GetProviderName(); }
        public void SetRotationManager(APIRotationManager manager) => _rotationManager = manager;
        public void SetRetryServiceFactory(Func<IAIService?>? factory) => _retryServiceFactory = factory;
        public void UpdateModelConfig(ModelConfig config) => _modelConfig = config;
        public void UpdateSystemPrompt(string prompt) => _systemPrompt = prompt ?? string.Empty;
        public string CurrentProvider => _aiService.GetProviderName();
        public string CurrentModel => _aiService is HostedManagedAiService hosted
            ? hosted.GetModelName()
            : _rotationManager?.GetCurrentModel(_currentProvider) ?? string.Empty;

        public void ConfigureCopilot(CopilotMode mode, InterviewDeliveryStyle style)
        {
            _copilotMode = mode;
            _deliveryStyle = style;
            LastDecision = null;
            LastModelCallCount = 0;
            PendingClarificationOptions = Array.Empty<ClarificationOption>();
        }

        public void SetResume(string text, string cachedSummary = "") => UpdateResume(text, cachedSummary);
        public void UpdateResume(string text, string cachedSummary = "")
        {
            text ??= string.Empty;
            if (_resumeText != text) { _resumeSummary = string.Empty; _resumeSummaryTask = null; }
            _resumeText = text;
            if (!string.IsNullOrWhiteSpace(cachedSummary)) _resumeSummary = cachedSummary;
        }
        public string GetResumeSummary() => _resumeSummary;
        public bool HasResume() => !string.IsNullOrWhiteSpace(_resumeText);

        public void SetJobDescription(string text, string cachedSummary = "") => UpdateJobDescription(text, cachedSummary);
        public void UpdateJobDescription(string text, string cachedSummary = "")
        {
            text ??= string.Empty;
            if (_jobDescriptionText != text) { _jobDescriptionSummary = string.Empty; _jobDescriptionSummaryTask = null; }
            _jobDescriptionText = text;
            if (!string.IsNullOrWhiteSpace(cachedSummary)) _jobDescriptionSummary = cachedSummary;
        }
        public void ClearJobDescription() { _jobDescriptionText = string.Empty; _jobDescriptionSummary = string.Empty; _jobDescriptionSummaryTask = null; }
        public string GetJobDescriptionSummary() => _jobDescriptionSummary;
        public bool HasJobDescription() => !string.IsNullOrWhiteSpace(_jobDescriptionText);

        public async Task PrepareContextAsync(CancellationToken cancellationToken = default)
        {
            if (HasResume() && string.IsNullOrWhiteSpace(_resumeSummary))
            {
                var source = _resumeText;
                _resumeSummaryTask ??= SummarizeAsync(
                    "Summarize this resume in under 150 words. Preserve only supported roles, dates, skills, projects, metrics, and achievements. Do not add facts.",
                    source, summary => { if (_resumeText == source) _resumeSummary = summary; });
                await _resumeSummaryTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                _resumeSummaryTask = null;
            }
            if (HasJobDescription() && string.IsNullOrWhiteSpace(_jobDescriptionSummary))
            {
                var source = _jobDescriptionText;
                _jobDescriptionSummaryTask ??= SummarizeAsync(
                    "Summarize this role or meeting context in under 100 words. Preserve requirements without treating them as candidate experience.",
                    source, summary => { if (_jobDescriptionText == source) _jobDescriptionSummary = summary; });
                await _jobDescriptionSummaryTask.WaitAsync(cancellationToken).ConfigureAwait(false);
                _jobDescriptionSummaryTask = null;
            }
        }

        public async Task WarmLiveContextAsync(CancellationToken cancellationToken = default)
        {
            await PrepareContextAsync(cancellationToken).ConfigureAwait(false);
            await LoadKnowledgeBaseSummaryAsync(cancellationToken).ConfigureAwait(false);
        }

        private async Task<bool> SummarizeAsync(string instruction, string source, Action<string> apply)
        {
            try
            {
                var result = await _aiService.SendMessageAsync(new() { Message("system", instruction), Message("user", source) }).ConfigureAwait(false);
                if (result.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return false;
                apply(result.Trim());
                return true;
            }
            catch { return false; }
        }

        public Task<(string response, string error)> SendMessageAsync(string userMessage, string? imageBase64 = null)
            => SendMessageStreamAsync(userMessage, _ => { }, CancellationToken.None, imageBase64);

        public async Task<(string response, string error)> SendMessageStreamAsync(
            string userMessage, Action<string> onChunkReceived, CancellationToken cancellationToken = default,
            string? imageBase64 = null, Action? onRetryCleanup = null)
        {
            PendingClarificationOptions = Array.Empty<ClarificationOption>();
            var user = Message("user", userMessage);
            CurrentHistory.Add(user);
            try
            {
                StageChanged?.Invoke(this, "Understanding…");
                var knowledge = await LoadKnowledgeBaseSummaryAsync(cancellationToken).ConfigureAwait(false);
                var resume = !string.IsNullOrWhiteSpace(_resumeSummary) ? _resumeSummary : _resumeText;
                var roleContext = !string.IsNullOrWhiteSpace(_jobDescriptionSummary) ? _jobDescriptionSummary : _jobDescriptionText;
                var firstPrompt = CopilotPromptRegistry.BuildFirstCallPrompt(
                    _copilotMode, _deliveryStyle, knowledge, resume, roleContext, _activeEvidence[_copilotMode]);
                var firstContext = BuildAdaptiveContext(firstPrompt);
                var repairPrompt = CopilotPromptRegistry.BuildFirstCallPrompt(
                    _copilotMode, _deliveryStyle, knowledge, resume, roleContext, _activeEvidence[_copilotMode], protocolRepair: true);
                var repairContext = BuildAdaptiveContext(repairPrompt);
                var firstProtocolAttempt = 0;
                LiveRequestTrace.Current?.SetContext("adaptive", imageBase64 != null, firstContext.Sum(x => x.EstimatedTokens));

                var result = await new LiveCopilotOrchestrator().ExecuteAsync(
                    CopilotPromptRegistry.EntityIds(knowledge, _copilotMode), CopilotPromptRegistry.DocumentIds(knowledge, _copilotMode),
                    (publish, cleanup, token) => RunModelOperationAsync(
                        "first_model", 1, ++firstProtocolAttempt == 1 ? firstContext : repairContext,
                        publish, cleanup, token, imageBase64),
                    async (decision, token) =>
                    {
                        StageChanged?.Invoke(this, "Searching your knowledge…");
                        LiveRequestTrace.Current?.StartOperation("retrieval", 0);
                        if (_knowledgeRetriever == null || _knowledgeRetrievalEnabled?.Invoke() == false)
                        {
                            LiveRequestTrace.Current?.CompleteOperation("retrieval_completed", "unavailable", snippetCount: 0);
                            return new("unavailable", Array.Empty<RetrievedContextSnippet>(), KnowledgeRevision(knowledge));
                        }
                        try
                        {
                            var preferredDocuments = decision.PreferredDocumentIds;
                            if (_copilotMode == CopilotMode.Briefing && preferredDocuments.Count == 0)
                                preferredDocuments = CopilotPromptRegistry.DocumentIds(knowledge, CopilotMode.Briefing);
                            if (_copilotMode == CopilotMode.Briefing && preferredDocuments.Count == 0)
                            {
                                LiveRequestTrace.Current?.CompleteOperation("retrieval_completed", "unavailable", snippetCount: 0);
                                return new("unavailable", Array.Empty<RetrievedContextSnippet>(), KnowledgeRevision(knowledge));
                            }
                            var snippets = (await _knowledgeRetriever(decision.RetrievalQuery, preferredDocuments, token).ConfigureAwait(false)).Take(3).ToArray();
                            var status = snippets.Length == 0 ? "empty" : "found";
                            LiveRequestTrace.Current?.CompleteOperation("retrieval_completed", status, snippetCount: snippets.Length);
                            return new(status, snippets, KnowledgeRevision(knowledge));
                        }
                        catch (OperationCanceledException) { throw; }
                        catch
                        {
                            LiveRequestTrace.Current?.CompleteOperation("retrieval_failed", "error", errorCode: "retrieval_failed");
                            return new("error", Array.Empty<RetrievedContextSnippet>(), KnowledgeRevision(knowledge));
                        }
                    },
                    (decision, retrieval) =>
                    {
                        var prompt = CopilotPromptRegistry.BuildSecondCallPrompt(
                            _copilotMode, _deliveryStyle, decision, retrieval, knowledge, resume, roleContext);
                        var context = BuildAdaptiveContext(prompt);
                        return (publish, cleanup, token) => RunModelOperationAsync("second_model", 2, context, publish, cleanup, token, imageBase64);
                    },
                    chunk => { StageChanged?.Invoke(this, "Answering…"); LiveRequestTrace.Current?.Mark("answer_first_visible_token"); onChunkReceived(chunk); },
                    onRetryCleanup, cancellationToken,
                    code => LiveRequestTrace.Current?.RejectControl(code),
                    (decision, calls) =>
                    {
                        LiveRequestTrace.Current?.SetDecision(
                            decision, calls, decision.Action == LiveCopilotAction.Retrieve ? "pending" : "not_requested");
                        DecisionParsed?.Invoke(decision, calls);
                    }).ConfigureAwait(false);

                LastDecision = result.Decision;
                LastModelCallCount = result.ModelCallCount;
                if (result.ActiveEvidence.Count > 0) _activeEvidence[_copilotMode] = result.ActiveEvidence;
                var assistant = Message("assistant", result.Answer);
                assistant.HasCode = result.Answer.Contains("```", StringComparison.Ordinal);
                assistant.AnswerSource = result.Decision.AnswerBasis;
                assistant.InterviewIntent = result.Decision.Intent;
                CurrentHistory.Add(assistant);
                return (result.Answer, string.Empty);
            }
            catch (OperationCanceledException) { CurrentHistory.Remove(user); return (string.Empty, "Cancelled"); }
            catch (PhantomProtocolException error)
            {
                CurrentHistory.Remove(user);
                LiveRequestTrace.Current?.RejectControl(error.Code);
                return (string.Empty, "The selected model returned an invalid live-response header. Please retry.");
            }
            catch (Exception error)
            {
                CurrentHistory.Remove(user);
                LiveRequestTrace.Current?.Fail("turn_failed", error is TimeoutException ? "timeout" : "provider_error");
                return (string.Empty, error is TimeoutException ? "The AI provider timed out. Please retry." : "The AI provider could not complete this request. Please retry.");
            }
        }

        private async Task<(string Response, string Error)> RunModelOperationAsync(
            string operation, int index, List<ConversationMessage> context, Action<string> publish,
            Action cleanup, CancellationToken token, string? imageBase64)
        {
            LiveRequestTrace.Current?.StartOperation(operation, index);
            var result = await SendWithRetryStreamAsync(context, publish, token, imageBase64, cleanup).ConfigureAwait(false);
            LiveRequestTrace.Current?.CompleteOperation("model_call_completed", string.IsNullOrEmpty(result.error) ? "success" : "error",
                string.IsNullOrEmpty(result.error) ? null : "provider_error");
            return (result.response, result.error);
        }

        private List<ConversationMessage> BuildAdaptiveContext(string prompt)
        {
            var result = new List<ConversationMessage> { Message("system", prompt) };
            var budget = Math.Max(1000, _modelConfig.MaxContextTokens - _modelConfig.MaxResponseTokens - result[0].EstimatedTokens);
            var selected = new List<ConversationMessage>();
            foreach (var item in CurrentHistory.AsEnumerable().Reverse())
            {
                var copy = Message(item.Role, Limit(item.Content, 2400));
                if (selected.Count > 0 && budget < copy.EstimatedTokens) break;
                selected.Add(copy);
                budget -= copy.EstimatedTokens;
                if (selected.Count >= 8) break;
            }
            selected.Reverse();
            result.AddRange(selected);
            return result.Where(x => !string.IsNullOrWhiteSpace(x.Content)).ToList();
        }

        private async Task<HostedKnowledgeBaseSummaryDto?> LoadKnowledgeBaseSummaryAsync(CancellationToken token)
        {
            if (_knowledgeBaseLoader == null || _knowledgeBaseSummary != null) return _knowledgeBaseSummary;
            _knowledgeBaseSummaryTask ??= LoadKnowledgeBaseSummaryCoreAsync(token);
            try { return await _knowledgeBaseSummaryTask.ConfigureAwait(false); }
            finally { if (_knowledgeBaseSummaryTask.IsCompleted) _knowledgeBaseSummaryTask = null; }
        }

        private async Task<HostedKnowledgeBaseSummaryDto?> LoadKnowledgeBaseSummaryCoreAsync(CancellationToken token)
        {
            try { return _knowledgeBaseSummary = await _knowledgeBaseLoader!(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch { return _knowledgeBaseSummary; }
        }

        private async Task<(string response, string error)> SendWithRetryStreamAsync(
            List<ConversationMessage> context, Action<string> publish, CancellationToken token,
            string? imageBase64, Action? cleanup)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var response = await _aiService.SendMessageStreamAsync(context, publish, token, imageBase64).ConfigureAwait(false);
                    if (!response.StartsWith("Error:", StringComparison.OrdinalIgnoreCase)) return (response, string.Empty);
                    if (attempt == maxAttempts || !IsRetryableError(response)) return (string.Empty, "provider_error");
                }
                catch (OperationCanceledException) { throw; }
                catch when (attempt < maxAttempts) { }
                catch { return (string.Empty, "provider_error"); }

                cleanup?.Invoke();
                LiveRequestTrace.Current?.MarkRetry();
                RotateProviderIfAvailable();
            }
            return (string.Empty, "provider_error");
        }

        private void RotateProviderIfAvailable()
        {
            try
            {
                if (_retryServiceFactory != null)
                {
                    var retryService = _retryServiceFactory();
                    if (retryService != null)
                    {
                        _aiService = retryService;
                        _currentProvider = retryService.GetProviderName();
                        var model = retryService is HostedManagedAiService hosted
                            ? hosted.GetModelName()
                            : _rotationManager?.GetCurrentModel(_currentProvider) ?? string.Empty;
                        LiveRequestTrace.Current?.RotateProvider(_currentProvider, model);
                        APISwitchNotification?.Invoke(this, "Switched managed provider or model after a retryable failure.");
                        return;
                    }
                }

                if (_rotationManager == null) return;
                var key = _rotationManager.GetNextApiKey(_currentProvider);
                _aiService = AIServiceFactory.CreateService(_currentProvider, key, _rotationManager.GetCurrentModel(_currentProvider));
                LiveRequestTrace.Current?.RotateProvider(_currentProvider, _rotationManager.GetCurrentModel(_currentProvider));
                APISwitchNotification?.Invoke(this, "Switched provider credential after a retryable failure.");
            }
            catch { }
        }

        private static bool IsRetryableError(string error) =>
            new[] { "429", "rate_limit", "capacity", "overloaded", "timeout", "500", "502", "503" }
                .Any(value => error.Contains(value, StringComparison.OrdinalIgnoreCase));

        public List<ConversationMessage> GetAllMessages() => CurrentHistory.ToList();
        public void CompleteLastAssistantTiming(int responseTimeMs)
        {
            var assistant = CurrentHistory.LastOrDefault(message => message.Role == "assistant");
            if (assistant == null) return;
            assistant.Timestamp = DateTime.UtcNow;
            assistant.ResponseTimeMs = Math.Max(0, responseTimeMs);
        }
        public string? RemoveLastExchangeForRegeneration()
        {
            if (CurrentHistory.Count < 2 || CurrentHistory[^1].Role != "assistant" || CurrentHistory[^2].Role != "user") return null;
            var question = CurrentHistory[^2].Content;
            CurrentHistory.RemoveRange(CurrentHistory.Count - 2, 2);
            return question;
        }
        public void ClearConversation()
        {
            foreach (var history in _histories.Values) history.Clear();
            _activeEvidence[CopilotMode.Interview] = Array.Empty<RetrievedContextSnippet>();
            _activeEvidence[CopilotMode.Briefing] = Array.Empty<RetrievedContextSnippet>();
            _knowledgeBaseSummary = null; _knowledgeBaseSummaryTask = null; LastDecision = null; LastModelCallCount = 0;
            _rotationManager?.StartNewConversation(_currentProvider);
        }
        public void StartNewTopic()
        {
            CurrentHistory.Clear(); _activeEvidence[_copilotMode] = Array.Empty<RetrievedContextSnippet>();
            LastDecision = null; LastModelCallCount = 0; _rotationManager?.StartNewConversation(_currentProvider);
        }
        public void ChangeProvider(string provider) { _currentProvider = provider; _rotationManager?.ResetAllConversationState(); }
        public int EstimateTokens(string text) => string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Ceiling(text.Length / 4d);
        public int GetTotalTokens() => CurrentHistory.Sum(x => x.EstimatedTokens);
        public int GetContextTokens() => BuildAdaptiveContext(_systemPrompt).Sum(x => x.EstimatedTokens);
        public bool CanSendMessage() => GetContextTokens() + _modelConfig.MaxResponseTokens < _modelConfig.MaxContextTokens;
        public List<ConversationMessage> GetOptimizedContextForDebug() => BuildAdaptiveContext(_systemPrompt);
        public List<ConversationMessage> ExportConversation() => CurrentHistory.Select(Clone).ToList();
        public void ImportConversation(List<ConversationMessage> messages)
        {
            CurrentHistory.Clear();
            CurrentHistory.AddRange((messages ?? new()).Where(x => x.Role is "user" or "assistant").Select(Clone));
        }
        public int GetMessageCount() => CurrentHistory.Count;

        private ConversationMessage Message(string role, string content) => new()
        {
            Role = role, Content = content ?? string.Empty, Timestamp = DateTime.UtcNow,
            EstimatedTokens = EstimateTokens(content ?? string.Empty)
        };
        private static ConversationMessage Clone(ConversationMessage source) => new()
        {
            Role = source.Role, Content = source.Content, Summary = source.Summary, Timestamp = source.Timestamp,
            HasCode = source.HasCode, EstimatedTokens = source.EstimatedTokens, AnswerSource = source.AnswerSource,
            InterviewIntent = source.InterviewIntent, ResponseTimeMs = source.ResponseTimeMs
        };
        private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
        private static string KnowledgeRevision(HostedKnowledgeBaseSummaryDto? knowledge) => knowledge?.EmbeddingVersion.ToString() ?? string.Empty;
        public sealed record ClarificationOption(string Label, string Question);
    }
}
