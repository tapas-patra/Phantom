using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public class ConversationManager
    {
        private const int RecentFullMessageCount = 10;

        private List<ConversationMessage> _fullConversation = new List<ConversationMessage>();
        private string _systemPrompt = "";
        private string _resumeText = string.Empty;
        private string _resumeSummary = string.Empty;
        private bool _resumeSummarized = false;
        private Task<bool>? _resumeSummaryTask;

        // NEW: Job Description fields
        private string _jobDescriptionText = string.Empty;
        private string _jobDescriptionSummary = string.Empty;
        private bool _jobDescriptionSummarized = false;
        private Task<bool>? _jobDescriptionSummaryTask;
        private string _structuredKnowledgeContext = string.Empty;
        private string _activeProjectCardId = string.Empty;
        private IReadOnlyList<RetrievedContextSnippet> _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
        private IReadOnlyList<string> _lastRetrievedDocumentIds = Array.Empty<string>();
        private readonly Func<string, IReadOnlyList<string>?, CancellationToken, Task<IReadOnlyList<RetrievedContextSnippet>>>? _knowledgeRetriever;
        private readonly Func<CancellationToken, Task<HostedKnowledgeBaseSummaryDto>>? _knowledgeBaseLoader;
        private readonly Func<bool>? _knowledgeRetrievalEnabled;
        private HostedKnowledgeBaseSummaryDto? _knowledgeBaseSummaryCache;
        private Task<HostedKnowledgeBaseSummaryDto?>? _knowledgeBaseSummaryLoadTask;
        private Task? _interviewContextPackWarmupTask;
        private string _knowledgeBaseRevision = string.Empty;
        private readonly Dictionary<string, CachedInterviewContextPack> _interviewContextPacks = new(StringComparer.Ordinal);
        private string _stablePromptPrefix = string.Empty;
        private string _stablePromptPrefixKey = string.Empty;

        
        private ModelConfig _modelConfig;
        private IAIService _aiService;


        private APIRotationManager? _rotationManager;
        private string _currentProvider = "";

        public event EventHandler<string>? APISwitchNotification;

        public ConversationManager(
            IAIService aiService,
            string systemPrompt,
            ModelConfig modelConfig,
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

            RunRouterSelfCheck();

            _fullConversation.Add(new ConversationMessage
            {
                Role = "system",
                Content = systemPrompt,
                EstimatedTokens = EstimateTokens(systemPrompt)
            });
        }

        // ═══════════════════════════════════════════════════════════════
        // UPDATE AI SERVICE & CONFIG WITHOUT LOSING HISTORY
        // ═══════════════════════════════════════════════════════════════

        // Update UpdateAIService to track provider
        public void UpdateAIService(IAIService aiService)
        {
            _aiService = aiService;
            _currentProvider = aiService.GetProviderName();
            Log.WriteLine($"✓ AI service updated to {aiService.GetProviderName()} - history preserved");
        }

        // Add method to update rotation manager
        public void SetRotationManager(APIRotationManager rotationManager)
        {
            _rotationManager = rotationManager;
        }

        public void UpdateModelConfig(ModelConfig config)
        {
            _modelConfig = config;
            Log.WriteLine($"✓ Model config updated: {config.Name} ({config.MaxContextTokens} tokens) - history preserved");
        }

        public void UpdateSystemPrompt(string prompt)
        {
            _systemPrompt = prompt;
            UpdateSystemPromptWithContext();
            
            Log.WriteLine("✓ System prompt updated - history preserved");
        }

        // ═══════════════════════════════════════════════════════════════
        // RESUME MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        public void SetResume(string resumeText, string cachedSummary = "")
        {
            _resumeText = resumeText;
            _resumeSummaryTask = null;

            if (!string.IsNullOrWhiteSpace(cachedSummary))
            {
                // Use cached summary
                _resumeSummary = cachedSummary;
                _resumeSummarized = true;
                Log.WriteLine($"✓ Loaded cached resume summary ({EstimateTokens(cachedSummary)} tokens)");
                
                // NEW: Update system prompt immediately when cached summary is available
                UpdateSystemPromptWithContext();
            }
            else if (!string.IsNullOrWhiteSpace(resumeText))
            {
                Log.WriteLine($"Resume set ({EstimateTokens(resumeText)} tokens) - will summarize on first message");
                _resumeSummarized = false;
            }
        }

        
        public void UpdateResume(string resumeText, string cachedSummary = "")
        {
            Log.WriteLine("Updating resume...");
            
            var oldResumeText = _resumeText;
            
            _resumeText = resumeText;
            
            // Check if resume actually changed
            if (oldResumeText != resumeText)
            {
                Log.WriteLine("Resume text changed - clearing old summary");
                
                // Clear old summary since resume changed
                _resumeSummary = "";
                _resumeSummarized = false;
                _resumeSummaryTask = null;
                
                if (!string.IsNullOrWhiteSpace(resumeText))
                {
                    // New resume - will be summarized on next message
                    if (!string.IsNullOrWhiteSpace(cachedSummary))
                    {
                        _resumeSummary = cachedSummary;
                        _resumeSummarized = true;
                        Log.WriteLine($"✓ Using provided cached summary ({EstimateTokens(cachedSummary)} tokens)");
                    }
                    else
                    {
                        Log.WriteLine($"✓ New resume set ({EstimateTokens(resumeText)} tokens) - will summarize on next message");
                    }
                }
                else
                {
                    Log.WriteLine("✓ Resume cleared");
                }
                
                // Update system prompt with new resume summary
                UpdateSystemPromptWithContext();
            }
            else if (string.IsNullOrWhiteSpace(oldResumeText) && !string.IsNullOrWhiteSpace(cachedSummary))
            {
                // Same resume but we have a new cached summary
                _resumeSummary = cachedSummary;
                _resumeSummarized = true;
                Log.WriteLine($"✓ Updated cached summary ({EstimateTokens(cachedSummary)} tokens)");
                
                UpdateSystemPromptWithContext();
            }
            else
            {
                Log.WriteLine("Resume unchanged");
            }
        }

        public string GetResumeSummary() => _resumeSummary;

        public bool HasResume() => !string.IsNullOrWhiteSpace(_resumeText);

        private async Task<bool> SummarizeResumeAsync(string sourceText)
        {
            if (_resumeSummarized || string.IsNullOrWhiteSpace(sourceText))
                return true;

            try
            {
                Log.WriteLine("Summarizing resume...");

                var summarizationPrompt = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                    Role = "system",
                    Content = @"You are an expert career advisor creating a structured professional summary. 
                Extract and summarize the most relevant information from the resume in this exact format:
                
                **Name:** [Extract full name from resume]
                **Role & Experience:** [Current/target role, years of experience, seniority level]
                **Core Skills:** [Top 5-7 technical skills, tools, or technologies - be specific]
                **Domain Expertise:** [Industries, specializations, or focus areas]
                **Key Strengths:** [Notable achievements, certifications, or unique capabilities]

                Guidelines:
                - Prioritize technical skills and concrete expertise over soft skills
                - Include specific technologies, frameworks, or methodologies mentioned
                - Keep the entire summary under 150 words
                - Use clear, direct language suitable for AI context understanding
                - Focus on information useful for answering interview questions or work-related queries"
                    },
                    new ConversationMessage
                    {
                        Role = "user",
                        Content = sourceText
                    }
                };

                var summary = await _aiService.SendMessageAsync(summarizationPrompt);

                if (!summary.StartsWith("Error:"))
                {
                    if (!string.Equals(_resumeText, sourceText, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    _resumeSummary = summary.Trim();
                    _resumeSummarized = true;
                    Log.WriteLine($"✓ Resume summarized ({EstimateTokens(_resumeSummary)} tokens)");
                    
                    // NEW: Update system prompt after summarization
                    UpdateSystemPromptWithContext();
                    
                    return true;
                }
                else
                {
                    Log.WriteLine($"✗ Resume summarization failed: {summary}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ Resume summarization error: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // JOB DESCRIPTION MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        public void SetJobDescription(string jdText, string cachedSummary = "")
        {
            Log.WriteLine("─────────────────────────────────────────────────────");
            Log.WriteLine("SetJobDescription() CALLED");
            Log.WriteLine($"  jdText length: {jdText?.Length ?? 0}");
            Log.WriteLine($"  cachedSummary length: {cachedSummary?.Length ?? 0}");
            
            _jobDescriptionText = jdText?? string.Empty;
            _jobDescriptionSummaryTask = null;

            if (!string.IsNullOrWhiteSpace(cachedSummary))
            {
                // Use cached summary
                _jobDescriptionSummary = cachedSummary;
                _jobDescriptionSummarized = true;
                Log.WriteLine($"✓ Loaded cached JD summary ({EstimateTokens(cachedSummary)} tokens)");
                
                // Update system prompt immediately when cached summary is available
                UpdateSystemPromptWithContext();
            }
            else if (!string.IsNullOrWhiteSpace(jdText))
            {
                Log.WriteLine($"JD set ({EstimateTokens(jdText)} tokens) - will summarize on first message");
                _jobDescriptionSummarized = false;
            }
            else
            {
                Log.WriteLine("✗ No JD text and no cached summary provided");
            }
            
            Log.WriteLine("─────────────────────────────────────────────────────");
        }



        public void UpdateJobDescription(string jdText, string cachedSummary = "")
        {
            Log.WriteLine("Updating job description...");
            
            var oldJdText = _jobDescriptionText;
            
            _jobDescriptionText = jdText;
            
            // Check if JD actually changed
            if (oldJdText != jdText)
            {
                Log.WriteLine("JD text changed - clearing old summary");
                
                // Clear old summary since JD changed
                _jobDescriptionSummary = "";
                _jobDescriptionSummarized = false;
                _jobDescriptionSummaryTask = null;
                
                if (!string.IsNullOrWhiteSpace(jdText))
                {
                    // New JD - will be summarized on next message
                    if (!string.IsNullOrWhiteSpace(cachedSummary))
                    {
                        _jobDescriptionSummary = cachedSummary;
                        _jobDescriptionSummarized = true;
                        Log.WriteLine($"✓ Using provided cached JD summary ({EstimateTokens(cachedSummary)} tokens)");
                    }
                    else
                    {
                        Log.WriteLine($"✓ New JD set ({EstimateTokens(jdText)} tokens) - will summarize on next message");
                    }
                }
                else
                {
                    Log.WriteLine("✓ JD cleared");
                }
                
                // Update system prompt with new JD summary
                UpdateSystemPromptWithContext();
            }
            else if (string.IsNullOrWhiteSpace(oldJdText) && !string.IsNullOrWhiteSpace(cachedSummary))
            {
                // Same JD but we have a new cached summary
                _jobDescriptionSummary = cachedSummary;
                _jobDescriptionSummarized = true;
                Log.WriteLine($"✓ Updated cached JD summary ({EstimateTokens(cachedSummary)} tokens)");
                
                UpdateSystemPromptWithContext();
            }
            else
            {
                Log.WriteLine("JD unchanged");
            }
        }

        public void ClearJobDescription()
        {
            Log.WriteLine("Clearing job description...");
            _jobDescriptionText = "";
            _jobDescriptionSummary = "";
            _jobDescriptionSummarized = false;
            _jobDescriptionSummaryTask = null;
            
            UpdateSystemPromptWithContext();
            
            Log.WriteLine("✓ Job description cleared");
        }

        public string GetJobDescriptionSummary() => _jobDescriptionSummary;

        public bool HasJobDescription() => !string.IsNullOrWhiteSpace(_jobDescriptionText);

        public async Task PrepareContextAsync(CancellationToken cancellationToken = default)
        {
            if (HasResume() && !_resumeSummarized)
            {
                var task = _resumeSummaryTask ??= SummarizeResumeAsync(_resumeText);
                try
                {
                    await task.WaitAsync(cancellationToken);
                }
                finally
                {
                    if (ReferenceEquals(_resumeSummaryTask, task) && task.IsCompleted)
                    {
                        _resumeSummaryTask = null;
                    }
                }
            }

            if (HasJobDescription() && !_jobDescriptionSummarized)
            {
                var task = _jobDescriptionSummaryTask ??= SummarizeJobDescriptionAsync(_jobDescriptionText);
                try
                {
                    await task.WaitAsync(cancellationToken);
                }
                finally
                {
                    if (ReferenceEquals(_jobDescriptionSummaryTask, task) && task.IsCompleted)
                    {
                        _jobDescriptionSummaryTask = null;
                    }
                }
            }
        }

        public async Task WarmLiveContextAsync(CancellationToken cancellationToken = default)
        {
            await PrepareContextAsync(cancellationToken);
            await LoadKnowledgeBaseSummaryAsync(cancellationToken);
        }

        private async Task<bool> SummarizeJobDescriptionAsync(string sourceText)
        {
            if (_jobDescriptionSummarized || string.IsNullOrWhiteSpace(sourceText))
                return true;

            try
            {
                Log.WriteLine("Summarizing job description...");

                var summarizationPrompt = new List<ConversationMessage>
                {
                    new ConversationMessage
                    {
                        Role = "system",
                        Content = "You are a helpful assistant that creates concise summaries. Summarize the following job description in 2-3 sentences, highlighting key requirements, responsibilities, and qualifications. Keep it under 100 words."
                    },
                    new ConversationMessage
                    {
                        Role = "user",
                        Content = sourceText
                    }
                };

                var summary = await _aiService.SendMessageAsync(summarizationPrompt);

                if (!summary.StartsWith("Error:"))
                {
                    if (!string.Equals(_jobDescriptionText, sourceText, StringComparison.Ordinal))
                    {
                        return false;
                    }

                    _jobDescriptionSummary = summary.Trim();
                    _jobDescriptionSummarized = true;
                    Log.WriteLine($"✓ JD summarized ({EstimateTokens(_jobDescriptionSummary)} tokens)");
                    
                    // NEW: Update system prompt after summarization
                    UpdateSystemPromptWithContext();
                    
                    return true;
                }
                else
                {
                    Log.WriteLine($"✗ JD summarization failed: {summary}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.WriteLine($"✗ JD summarization error: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SYSTEM PROMPT CONTEXT BUILDER
        // ═══════════════════════════════════════════════════════════════

        private void UpdateSystemPromptWithContext()
        {
            var contextParts = new System.Text.StringBuilder(GetStablePromptPrefix());

            if (!string.IsNullOrWhiteSpace(_structuredKnowledgeContext))
            {
                contextParts.Append("\n\nInterview Grounding:\n");
                contextParts.Append(_structuredKnowledgeContext);
            }

            if (_retrievedKnowledgeSnippets.Count > 0)
            {
                contextParts.Append("\n\nRelevant Local Context:");
                foreach (var snippet in _retrievedKnowledgeSnippets)
                {
                    contextParts.Append($"\n- [{snippet.SourceType}] {snippet.DocumentTitle}: {snippet.Text}");
                }
            }

            var finalContent = contextParts.ToString();

            if (_fullConversation.Count == 0)
            {
                _fullConversation.Add(new ConversationMessage
                {
                    Role = "system",
                    Content = finalContent,
                    EstimatedTokens = EstimateTokens(finalContent)
                });
            }
            else
            {
                if (string.Equals(_fullConversation[0].Content, finalContent, StringComparison.Ordinal))
                {
                    return;
                }

                _fullConversation[0].Content = finalContent;
                _fullConversation[0].EstimatedTokens = EstimateTokens(finalContent);
            }
        }

        private string GetStablePromptPrefix()
        {
            var stablePromptKey = string.Join(
                "\u001f",
                new[]
                {
                    _systemPrompt,
                    _resumeSummary,
                    _jobDescriptionSummary,
                    _resumeSummarized ? string.Empty : TruncateMessageStatic(_resumeText, 2000),
                    _jobDescriptionSummarized ? string.Empty : TruncateMessageStatic(_jobDescriptionText, 1200)
                });
            if (string.Equals(_stablePromptPrefixKey, stablePromptKey, StringComparison.Ordinal))
            {
                return _stablePromptPrefix;
            }

            var contextParts = new System.Text.StringBuilder();
            contextParts.Append(_systemPrompt);

            if (!string.IsNullOrWhiteSpace(_resumeSummary))
            {
                contextParts.Append($"\n\nUser Profile: {_resumeSummary}");
            }
            else if (!string.IsNullOrWhiteSpace(_resumeText))
            {
                contextParts.Append($"\n\nUser Profile (raw fallback): {TruncateMessageStatic(_resumeText, 2000)}");
            }

            if (!string.IsNullOrWhiteSpace(_jobDescriptionSummary))
            {
                contextParts.Append("\n\nInterview Context: You are helping the user prepare for an interview for the following position. Provide relevant advice, practice questions, and feedback based on this job description:\n");
                contextParts.Append(_jobDescriptionSummary);
            }
            else if (!string.IsNullOrWhiteSpace(_jobDescriptionText))
            {
                contextParts.Append("\n\nInterview Context (raw fallback):\n");
                contextParts.Append(TruncateMessageStatic(_jobDescriptionText, 1200));
            }

            _stablePromptPrefix = contextParts.ToString();
            _stablePromptPrefixKey = stablePromptKey;
            return _stablePromptPrefix;
        }

        // ═══════════════════════════════════════════════════════════════
        // MESSAGE HANDLING (UPDATED WITH IMAGE SUPPORT)
        // ═══════════════════════════════════════════════════════════════

        public async Task<(string response, string error)> SendMessageAsync(string userMessage, string? imageBase64 = null)
        {
            // Add user message to full conversation
            var userMsg = new ConversationMessage
            {
                Role = "user",
                Content = userMessage,
                EstimatedTokens = EstimateTokens(userMessage)
            };
            _fullConversation.Add(userMsg);

            // Log current state
            Log.WriteLine("─────────────────────────────────────────────────────");
            Log.WriteLine($"Message #{_fullConversation.Count} in conversation");
            Log.WriteLine($"Using model: {_rotationManager?.GetCurrentModel(_currentProvider) ?? "unknown"}");
            if (imageBase64 != null)
            {
                Log.WriteLine($"📸 Image attached ({imageBase64.Length} chars)");
            }
            Log.WriteLine("─────────────────────────────────────────────────────");

            var retrievalEnabled = _knowledgeRetrievalEnabled?.Invoke() ?? true;
            if (retrievalEnabled)
            {
                retrievalEnabled = (await LoadKnowledgeBaseSummaryAsync(CancellationToken.None))?.CanUseInInterview == true;
            }
            var route = retrievalEnabled ? RouteResponse(userMessage) : ResponsePlan.Direct(string.Empty, 1d);
            if (!retrievalEnabled)
            {
                RagTraceLogger.WriteLine("router:direct rag_disabled=true");
            }
            await ApplyRouteAsync(route, userMessage, CancellationToken.None);

            var retrievalMissResponse = BuildRetrievalMissResponse(route);
            if (!string.IsNullOrWhiteSpace(retrievalMissResponse))
            {
                RagTraceLogger.WriteLine("retrieval:miss short_circuit_response=true");
                return CompleteAssistantResponse(retrievalMissResponse);
            }

            if (route.Type == ResponsePlanType.Direct
                && !string.IsNullOrWhiteSpace(route.DirectAnswer))
            {
                return CompleteAssistantResponse(route.DirectAnswer);
            }

            // Build optimized context for API
            var optimizedContext = BuildOptimizedContext();

            // Log context info
            var totalTokens = optimizedContext.Sum(m => m.EstimatedTokens);
            LiveRequestTrace.Current?.SetContext(route.Type.ToString(), route.Type == ResponsePlanType.Retrieve || _retrievedKnowledgeSnippets.Count > 0, totalTokens);
            Log.WriteLine($"Sending {optimizedContext.Count} messages ({totalTokens} tokens) to AI");

            // ✅ Send with image support
            var response = await SendWithRetryAsync(optimizedContext, imageBase64);

            // Check if response is an error
            if (response.StartsWith("Error:") || response.StartsWith("⚠️"))
            {
                // Remove the user message since request failed
                _fullConversation.RemoveAt(_fullConversation.Count - 1);
                return ("", response);
            }

            // Extract summary from response
            return CompleteAssistantResponse(response);
        }



        public async Task<(string response, string error)> SendMessageStreamAsync(
            string userMessage, 
            Action<string> onChunkReceived,
            CancellationToken cancellationToken = default,
            string? imageBase64 = null,
            Action? onRetryCleanup = null)
        {
            // Add user message to full conversation
            var userMsg = new ConversationMessage
            {
                Role = "user",
                Content = userMessage,
                EstimatedTokens = EstimateTokens(userMessage)
            };
            _fullConversation.Add(userMsg);

            // Log current state
            Log.WriteLine("─────────────────────────────────────────────────────");
            Log.WriteLine($"Message #{_fullConversation.Count} in conversation [STREAMING]");
            Log.WriteLine($"Using model: {_rotationManager?.GetCurrentModel(_currentProvider) ?? "unknown"}");
            if (imageBase64 != null)
            {
                Log.WriteLine($"📸 Image attached ({imageBase64.Length} chars)");
            }
            Log.WriteLine("─────────────────────────────────────────────────────");

            var retrievalEnabled = _knowledgeRetrievalEnabled?.Invoke() ?? true;
            if (retrievalEnabled)
            {
                retrievalEnabled = (await LoadKnowledgeBaseSummaryAsync(cancellationToken))?.CanUseInInterview == true;
            }
            var route = retrievalEnabled ? RouteResponse(userMessage) : ResponsePlan.Direct(string.Empty, 1d);
            if (!retrievalEnabled)
            {
                RagTraceLogger.WriteLine("router:direct rag_disabled=true [streaming]");
            }
            await ApplyRouteAsync(route, userMessage, cancellationToken);

            var retrievalMissResponse = BuildRetrievalMissResponse(route);
            if (!string.IsNullOrWhiteSpace(retrievalMissResponse))
            {
                RagTraceLogger.WriteLine("retrieval:miss short_circuit_response=true [streaming]");
                onChunkReceived?.Invoke(retrievalMissResponse);
                return CompleteAssistantResponse(retrievalMissResponse);
            }

            if (route.Type == ResponsePlanType.Direct
                && !string.IsNullOrWhiteSpace(route.DirectAnswer))
            {
                onChunkReceived?.Invoke(route.DirectAnswer);
                return CompleteAssistantResponse(route.DirectAnswer);
            }

            // Build optimized context for API
            var optimizedContext = BuildOptimizedContext();

            var totalTokens = optimizedContext.Sum(m => m.EstimatedTokens);
            LiveRequestTrace.Current?.SetContext(route.Type.ToString(), route.Type == ResponsePlanType.Retrieve || _retrievedKnowledgeSnippets.Count > 0, totalTokens);
            Log.WriteLine($"Sending {optimizedContext.Count} messages ({totalTokens} tokens) to AI [STREAMING]");

            // ✅ Send with cancellation support AND image
            var (response, error) = await SendWithRetryStreamAsync(
                optimizedContext,
                onChunkReceived,
                cancellationToken,
                imageBase64,
                onRetryCleanup);

            if (!string.IsNullOrEmpty(error))
            {
                _fullConversation.RemoveAt(_fullConversation.Count - 1);
                return ("", error);
            }

            return CompleteAssistantResponse(response);
        }



        // ═══════════════════════════════════════════════════════════════
        // OPTIMIZED CONTEXT BUILDING (Keep recent messages in full)
        // ═══════════════════════════════════════════════════════════════

        private List<ConversationMessage> BuildOptimizedContext()
        {
            var context = new List<ConversationMessage>();
            int tokenBudget = _modelConfig.MaxContextTokens - _modelConfig.MaxResponseTokens;

            // 1. Always include system prompt (which already contains resume + JD via UpdateSystemPromptWithContext())
            context.Add(_fullConversation[0]); // System prompt with resume + JD already embedded
            tokenBudget -= _fullConversation[0].EstimatedTokens;

            // REMOVED: The old code that manually added resume summary here (it's already in _fullConversation[0])
            // REMOVED: No need to manually add JD either (it's already in _fullConversation[0])

            // 2. Get user messages (skip system prompt at index 0)
            var userMessages = _fullConversation.Skip(1).Where(m => m.Role == "user" || m.Role == "assistant").ToList();

            if (userMessages.Count == 0)
                return context;

            // 3. Keep the latest suffix of messages in full, up to the recent-message cap.
            var recentMessagesReversed = new List<ConversationMessage>();
            foreach (var msg in userMessages.AsEnumerable().Reverse())
            {
                if (recentMessagesReversed.Count >= RecentFullMessageCount)
                {
                    break;
                }

                var wouldLeaveHeadroom = tokenBudget - msg.EstimatedTokens >= 100;
                if (recentMessagesReversed.Count > 0 && !wouldLeaveHeadroom)
                {
                    break;
                }

                recentMessagesReversed.Add(msg);
                tokenBudget -= msg.EstimatedTokens;
            }

            recentMessagesReversed.Reverse();
            var recentMessages = recentMessagesReversed;
            var recentTokens = recentMessages.Sum(m => m.EstimatedTokens);

            Log.WriteLine($"Reserved {recentTokens} tokens for last {recentMessages.Count} messages (full content)");

            // 4. Build sliding window for OLDER messages (with summaries)
            var slidingWindow = new List<ConversationMessage>();
            var olderMessages = userMessages
                .Take(Math.Max(0, userMessages.Count - recentMessages.Count))
                .Reverse()
                .ToList();

            int pairsIncluded = 0;
            foreach (var msg in olderMessages)
            {
                // Stop if we've included enough pairs
                if (pairsIncluded >= _modelConfig.SlidingWindowSize)
                    break;

                // Determine what to include (use summaries for older messages)
                string contentToUse;
                int tokensNeeded;

                if (!string.IsNullOrWhiteSpace(msg.Summary))
                {
                    // Use summary for older messages
                    contentToUse = msg.Summary;
                    tokensNeeded = EstimateTokens(msg.Summary);
                }
                else
                {
                    // No summary available, use truncated content
                    contentToUse = TruncateMessage(msg.Content, 200);
                    tokensNeeded = EstimateTokens(contentToUse);
                }

                // Check if we have budget
                if (tokenBudget - tokensNeeded < 100)
                    break;

                slidingWindow.Add(new ConversationMessage
                {
                    Role = msg.Role,
                    Content = contentToUse,
                    EstimatedTokens = tokensNeeded
                });

                tokenBudget -= tokensNeeded;

                if (msg.Role == "assistant")
                    pairsIncluded++;
            }

            // 5. Reverse to get chronological order
            slidingWindow.Reverse();
            context.AddRange(slidingWindow);

            // 6. Add the recent messages in FULL
            context.AddRange(recentMessages);

            Log.WriteLine($"Context built: {context.Count} messages, ~{context.Sum(m => m.EstimatedTokens)} tokens");
            Log.WriteLine($"  - System: 1 message (includes resume + JD)");
            Log.WriteLine($"  - Older (summarized): {slidingWindow.Count} messages");
            Log.WriteLine($"  - Recent (full): {recentMessages.Count} messages");

            var validatedContext = ValidateAndCleanMessages(context);
            return validatedContext;
        }

        private (string response, string error) CompleteAssistantResponse(string response)
        {
            var (fullResponse, summary, hasCode) = ParseAIResponse(response);
            _fullConversation.Add(new ConversationMessage
            {
                Role = "assistant",
                Content = fullResponse,
                Summary = summary,
                HasCode = hasCode,
                EstimatedTokens = EstimateTokens(fullResponse)
            });

            Log.WriteLine($"✓ Response added to conversation ({_fullConversation.Count} total messages)");
            return (fullResponse, "");
        }

        private ResponsePlan RouteResponse(string userMessage)
        {
            var normalized = NormalizeText(userMessage);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return ResponsePlan.Direct(string.Empty, 1d);
            }

            if (TryBuildHeuristicProjectPlan(userMessage, normalized, out var plan))
            {
                return TraceRoute(plan);
            }

            if (TryBuildHeuristicExperiencePlan(userMessage, normalized, out plan))
            {
                return TraceRoute(plan);
            }

            if (LooksLikeExplicitDocumentQuestion(normalized))
            {
                var scope = _lastRetrievedDocumentIds.Count > 0
                    && ContainsAnyToken(normalized, "that document", "those documents", "these notes", "those notes", "same document")
                        ? RetrievalScope.PreviousDocuments
                        : RetrievalScope.Global;
                return TraceRoute(ResponsePlan.Retrieve(userMessage, "deterministic-document", scope, confidence: 1d));
            }

            if (TryBuildHeuristicProfilePlan(userMessage, normalized, out plan))
            {
                return TraceRoute(plan);
            }

            return TraceRoute(ResponsePlan.Direct(string.Empty, 1d));
        }

        [Conditional("DEBUG")]
        private void RunRouterSelfCheck()
        {
            var previousSummary = _knowledgeBaseSummaryCache;
            var previousActiveProjectId = _activeProjectCardId;
            var project = new HostedKnowledgeBaseProjectCardDto
            {
                ProjectCardId = "router-check-atlas",
                Title = "Atlas Payments",
                Slug = "atlas-payments",
                IsRecent = true,
                Summary = "Payment routing"
            };
            var alternateProject = new HostedKnowledgeBaseProjectCardDto
            {
                ProjectCardId = "router-check-orion",
                Title = "Orion Search",
                Slug = "orion-search",
                Summary = "Search platform"
            };
            var currentExperience = new HostedKnowledgeBaseExperienceCardDto
            {
                ExperienceCardId = "router-check-current-experience",
                Company = "Current Co",
                Role = "API Automation Engineer",
                IsCurrent = true,
                Responsibilities = "API automation and service testing",
                Skills = new[] { "API automation" }
            };
            var previousExperience = new HostedKnowledgeBaseExperienceCardDto
            {
                ExperienceCardId = "router-check-previous-experience",
                Company = "Previous Co",
                Role = "UI Automation Engineer",
                Responsibilities = "UI automation with Selenium",
                Skills = new[] { "UI automation", "Selenium" }
            };
            _knowledgeBaseSummaryCache = new HostedKnowledgeBaseSummaryDto
            {
                ProjectCards = new[] { project, alternateProject },
                ExperienceCards = new[] { currentExperience, previousExperience }
            };
            _activeProjectCardId = project.ProjectCardId;

            var checks = new[]
            {
                ("Explain dependency injection", ResponsePlanType.Direct),
                ("Tell me about yourself", ResponsePlanType.Profile),
                ("What are my strengths?", ResponsePlanType.Profile),
                ("What are your day-to-day responsibilities?", ResponsePlanType.Experience),
                ("Have you worked with UI automation?", ResponsePlanType.Experience),
                ("Tell me about my recent project", ResponsePlanType.Project),
                ("Why did you choose that database?", ResponsePlanType.Project),
                ("Show the exact deployment details from my notes", ResponsePlanType.Retrieve),
                ("Give me an example", ResponsePlanType.Direct),
                ("Tell me about Atlas Payments", ResponsePlanType.Project),
                ("Tell me about any project you worked on", ResponsePlanType.Project),
                ("Tell me about any other project you worked on", ResponsePlanType.Project)
            };

            foreach (var (question, expected) in checks)
            {
                var actual = RouteResponse(question).Type;
                if (actual != expected)
                {
                    throw new InvalidOperationException($"Router self-check failed for '{question}': expected {expected}, got {actual}.");
                }
            }

            if (!string.IsNullOrWhiteSpace(RouteResponse("Tell me about any project you worked on").Target))
            {
                throw new InvalidOperationException("Router self-check failed: generic project request was treated as a named project.");
            }

            var alternateRoute = RouteResponse("Tell me about any other project you worked on");
            if (!string.Equals(
                    SelectProjectCard(new[] { project, alternateProject }, alternateRoute, "Tell me about any other project you worked on")?.ProjectCardId,
                    alternateProject.ProjectCardId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Router self-check failed: alternate-project request reused the active project.");
            }

            var unknownRoute = RouteResponse("Tell me about Orion project");
            if (unknownRoute.Type != ResponsePlanType.Project
                || SelectProjectCard(new[] { project }, unknownRoute, "Tell me about Orion project") != null)
            {
                throw new InvalidOperationException("Router self-check failed: unknown project selected an unrelated project.");
            }

            if (SelectExperienceCard(new[] { currentExperience, previousExperience }, "What are your day-to-day responsibilities?")?.ExperienceCardId
                    != currentExperience.ExperienceCardId
                || SelectExperienceCard(new[] { currentExperience, previousExperience }, "Have you worked with UI automation?")?.ExperienceCardId
                    != previousExperience.ExperienceCardId)
            {
                throw new InvalidOperationException("Router self-check failed: experience relevance did not override current-role priority when appropriate.");
            }

            _knowledgeBaseSummaryCache = previousSummary;
            _activeProjectCardId = previousActiveProjectId;
        }

        private static bool LooksLikeExplicitDocumentQuestion(string normalizedUserMessage)
        {
            if (ContainsAnyToken(normalizedUserMessage, "uploaded document", "uploaded file", "knowledge base", "my notes", "from my notes", "these notes", "those notes"))
            {
                return true;
            }

            return ContainsAnyToken(normalizedUserMessage, "exact", "specific", "implementation", "deployment", "line", "detail", "details")
                && ContainsAnyToken(normalizedUserMessage, "resume", "document", "file", "notes");
        }

        private static ResponsePlan TraceRoute(ResponsePlan plan)
        {
            RagTraceLogger.WriteLine(
                $"router:decision type={plan.Type} scope={plan.Scope} target='{TrimForLog(plan.Target, 120)}' knowledge_query='{TrimForLog(plan.KnowledgeQuery, 240)}'");
            return plan;
        }

        private bool TryBuildHeuristicProfilePlan(
            string userMessage,
            string normalizedUserMessage,
            out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "heuristic-none");
            if (!LooksLikeProfileQuestion(normalizedUserMessage))
            {
                return false;
            }

            var knowledgeQuery =
                ContainsAnyToken(normalizedUserMessage, "strength", "strengths")
                    ? "candidate strengths skills experience examples"
                    : ContainsAnyToken(normalizedUserMessage, "weakness", "weaknesses", "improve", "improvement")
                        ? "candidate weaknesses growth areas examples"
                        : ContainsAnyToken(normalizedUserMessage, "current role", "current position", "responsibilities", "responsibility")
                            ? "candidate current role responsibilities experience summary"
                            : "candidate background summary experience skills current role";
            plan = ResponsePlan.Profile(
                knowledgeQuery,
                target: string.Empty,
                scope: RetrievalScope.Global,
                confidence: 1d,
                source: "heuristic-profile");
            return true;
        }

        private bool TryBuildHeuristicExperiencePlan(
            string userMessage,
            string normalizedUserMessage,
            out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "heuristic-none");
            var personalAsk = ContainsAnyToken(
                normalizedUserMessage,
                "your experience", "my experience", "did you", "have you", "your role", "my role",
                "current role", "current position", "day to day", "day-to-day", "responsibilities",
                "company", "employer", "worked at", "work at");
            if (!personalAsk)
            {
                return false;
            }

            plan = ResponsePlan.Experience(userMessage, confidence: 1d, source: "heuristic-experience");
            return true;
        }

        private bool TryBuildHeuristicProjectPlan(
            string userMessage,
            string normalizedUserMessage,
            out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "heuristic-none");
            var alternateProjectAsk = IsAlternateProjectRequest(normalizedUserMessage);
            var namedProjectTarget = alternateProjectAsk ? string.Empty : ExtractLikelyProjectTarget(normalizedUserMessage);
            if (!alternateProjectAsk && string.IsNullOrWhiteSpace(namedProjectTarget))
            {
                namedProjectTarget = ExtractExplicitProjectTarget(normalizedUserMessage);
            }
            var namedProjectAsk = !string.IsNullOrWhiteSpace(namedProjectTarget);
            var hasActiveProject = !string.IsNullOrWhiteSpace(_activeProjectCardId);
            var explicitProjectAsk =
                ContainsAnyToken(
                    normalizedUserMessage,
                    "project",
                    "projects",
                    "recent project",
                    "what did you build",
                    "what have you built",
                    "what did you work on",
                    "worked on",
                    "tell me about a project",
                    "tell me about your project")
                || namedProjectAsk;
            var projectDetailAsk =
                ContainsAnyToken(normalizedUserMessage, "architecture", "system design", "tech stack", "stack", "challenge", "challenges", "impact", "role")
                && (ContainsAnyToken(normalizedUserMessage, "project", "projects", "this", "that", "it", "built", "worked on") || hasActiveProject || namedProjectAsk);
            var activeProjectFollowUp =
                hasActiveProject
                && ContainsAnyToken(normalizedUserMessage, "this", "that", "it", "the project", "architecture", "design", "stack", "challenge", "impact", "role")
                && !ContainsAnyToken(normalizedUserMessage, "yourself", "background", "resume", "strength", "weakness", "current role");
            if (!explicitProjectAsk && !projectDetailAsk && !activeProjectFollowUp && !namedProjectAsk)
            {
                return false;
            }

            var scope = activeProjectFollowUp ? RetrievalScope.ActiveProject : RetrievalScope.Global;
            var knowledgeQuery =
                ContainsAnyToken(normalizedUserMessage, "challenge", "challenges", "difficult", "problem", "tradeoff")
                    ? "project challenges tradeoffs impact role"
                    : ContainsAnyToken(normalizedUserMessage, "architecture", "design", "system design", "scalability", "performance", "security")
                        ? "project architecture technologies design tradeoffs impact"
                        : ContainsAnyToken(normalizedUserMessage, "stack", "tech stack", "technology", "tools", "framework")
                            ? "project technologies stack architecture role"
                            : "recent project architecture technologies impact role";
            plan = ResponsePlan.Project(
                knowledgeQuery,
                target: namedProjectTarget,
                scope: scope,
                confidence: 1d,
                source: "heuristic-project");
            if (alternateProjectAsk)
            {
                plan = ResponsePlan.Project(
                    "alternative work project overview architecture technologies impact role",
                    target: string.Empty,
                    scope: RetrievalScope.Global,
                    confidence: 1d,
                    source: "heuristic-alternate-project");
            }
            return true;
        }

        private static bool IsAlternateProjectRequest(string normalizedUserMessage)
            => ContainsAnyToken(
                normalizedUserMessage,
                "any other project",
                "another project",
                "other project",
                "different project");

        private bool LooksLikeProfileQuestion(string normalizedUserMessage)
        {
            if (ContainsAnyToken(
                    normalizedUserMessage,
                    "introduce yourself",
                    "tell me about yourself",
                    "walk me through your background",
                    "summarize your experience",
                    "your background",
                    "your experience",
                    "your resume",
                    "current role",
                    "current position",
                    "your strength",
                    "your strengths",
                    "my strength",
                    "my strengths",
                    "your weakness",
                    "your weaknesses",
                    "biggest strength",
                    "biggest weakness",
                    "your skills",
                    "why should we hire you"))
            {
                return true;
            }

            return normalizedUserMessage.StartsWith("who are you", StringComparison.Ordinal)
                || normalizedUserMessage.StartsWith("walk me through", StringComparison.Ordinal)
                || normalizedUserMessage.StartsWith("tell me about your background", StringComparison.Ordinal)
                || normalizedUserMessage.StartsWith("tell me about your experience", StringComparison.Ordinal);
        }

        private string ExtractLikelyProjectTarget(string normalizedUserMessage)
        {
            var projectCards = _knowledgeBaseSummaryCache?.ProjectCards;
            if (projectCards == null || projectCards.Count == 0)
            {
                return string.Empty;
            }

            foreach (var card in projectCards)
            {
                var title = NormalizeText(card.Title);
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                var slug = NormalizeText(card.Slug);
                if (!string.IsNullOrWhiteSpace(slug)
                    && normalizedUserMessage.Contains(slug, StringComparison.Ordinal))
                {
                    return card.Title;
                }

                if (normalizedUserMessage.Contains(title, StringComparison.Ordinal))
                {
                    return card.Title;
                }

                var titleTokens = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (titleTokens.Length >= 2 && titleTokens.All(token => normalizedUserMessage.Contains(token, StringComparison.Ordinal)))
                {
                    return card.Title;
                }
            }

            return string.Empty;
        }

        private static string ExtractExplicitProjectTarget(string normalizedUserMessage)
        {
            var match = Regex.Match(normalizedUserMessage, @"\bproject\s+(?:called|named)\s+(?<target>[a-z0-9 ]{2,60})$");
            if (!match.Success)
            {
                match = Regex.Match(normalizedUserMessage, @"\babout\s+(?<target>[a-z0-9 ]{2,60}?)\s+project\b");
            }

            if (!match.Success)
            {
                return string.Empty;
            }

            var target = match.Groups["target"].Value.Trim();
            return new[] { "my", "your", "a", "any", "the", "recent", "latest", "current", "my recent", "your recent" }.Contains(target, StringComparer.Ordinal)
                ? string.Empty
                : target;
        }

        private async Task ApplyRouteAsync(
            ResponsePlan route,
            string userMessage,
            CancellationToken cancellationToken)
        {
            switch (route.Type)
            {
                case ResponsePlanType.Direct:
                    ClearStructuredKnowledgeContext();
                    ClearRetrievedKnowledgeSnippets();
                    return;

                case ResponsePlanType.Profile:
                    ClearRetrievedKnowledgeSnippets();
                    await PrepareProfileGroundingAsync(route, userMessage, cancellationToken);
                    return;

                case ResponsePlanType.Project:
                    await PrepareProjectGroundingAsync(route, userMessage, cancellationToken);
                    return;

                case ResponsePlanType.Experience:
                    await PrepareExperienceGroundingAsync(userMessage, cancellationToken);
                    return;

                case ResponsePlanType.Retrieve:
                default:
                    ClearStructuredKnowledgeContext();
                    await RefreshRetrievedKnowledgeSnippetsAsync(
                        route.KnowledgeQuery,
                        route.Scope == RetrievalScope.PreviousDocuments ? _lastRetrievedDocumentIds : null,
                        cancellationToken);
                    return;
            }
        }

        private async Task PrepareProfileGroundingAsync(
            ResponsePlan plannerDecision,
            string userMessage,
            CancellationToken cancellationToken)
        {
            var knowledgeBase = await LoadKnowledgeBaseSummaryAsync(cancellationToken);
            var profileCard = knowledgeBase?.ProfileCard;
            var currentExperience = knowledgeBase?.ExperienceCards?.FirstOrDefault(card => card.IsCurrent);
            if (!HasProfileGrounding(profileCard))
            {
                _structuredKnowledgeContext = currentExperience == null
                    ? string.Empty
                    : BuildExperienceGrounding(currentExperience);
                RagTraceLogger.WriteLine(currentExperience == null
                    ? "profile_grounding:missing"
                    : "profile_grounding:fallback=current_experience");
                UpdateSystemPromptWithContext();
                return;
            }

            var groundedProfile = profileCard!;
            var packKind = DetermineProfilePackKind(plannerDecision, userMessage);
            if (TryApplyInterviewContextPack(BuildProfilePackCacheKey(packKind), out _))
            {
                RagTraceLogger.WriteLine($"profile_grounding:pack_used kind={packKind}");
                return;
            }

            _structuredKnowledgeContext = BuildProfileGrounding(groundedProfile, packKind);
            if (packKind == InterviewPackKind.ProfileIntro)
            {
                if (currentExperience != null)
                {
                    _structuredKnowledgeContext += "\n" + BuildExperienceGrounding(currentExperience);
                }
            }
            RagTraceLogger.WriteLine(
                $"profile_grounding:ready full_name='{TrimForLog(groundedProfile.FullName, 80)}' skills={groundedProfile.Skills.Count} strengths={groundedProfile.Strengths.Count} pack={packKind}");
            UpdateSystemPromptWithContext();
        }

        private async Task PrepareProjectGroundingAsync(
            ResponsePlan plannerDecision,
            string userMessage,
            CancellationToken cancellationToken)
        {
            var knowledgeBase = await LoadKnowledgeBaseSummaryAsync(cancellationToken);
            var selectedProject = SelectProjectCard(
                knowledgeBase?.ProjectCards ?? Array.Empty<HostedKnowledgeBaseProjectCardDto>(),
                plannerDecision,
                userMessage);
            if (!HasProjectGrounding(selectedProject))
            {
                if (string.IsNullOrWhiteSpace(plannerDecision.Target))
                {
                    _activeProjectCardId = string.Empty;
                    ClearRetrievedKnowledgeSnippets();
                    var profile = knowledgeBase?.ProfileCard;
                    _structuredKnowledgeContext = HasProfileWorkEvidence(profile)
                        ? BuildProfileProjectFallbackGrounding(profile!)
                        : BuildGenericProjectFallbackGrounding();
                    RagTraceLogger.WriteLine(
                        HasProfileWorkEvidence(profile)
                            ? "project_grounding:fallback=profile_work"
                            : "project_grounding:fallback=generic_fresher");
                    UpdateSystemPromptWithContext();
                    return;
                }

                ClearStructuredKnowledgeContext();
                ClearRetrievedKnowledgeSnippets();
                RagTraceLogger.WriteLine("project_grounding:missing");
                return;
            }

            var groundedProject = selectedProject!;
            if (!string.IsNullOrWhiteSpace(_activeProjectCardId)
                && !string.Equals(_activeProjectCardId, groundedProject.ProjectCardId, StringComparison.Ordinal))
            {
                ClearRetrievedKnowledgeSnippets();
            }
            _activeProjectCardId = groundedProject.ProjectCardId;
            var packKind = DetermineProjectPackKind(plannerDecision, userMessage);
            if (TryApplyInterviewContextPack(BuildProjectPackCacheKey(groundedProject.ProjectCardId, packKind), out var cachedPack))
            {
                _activeProjectCardId = groundedProject.ProjectCardId;
                RagTraceLogger.WriteLine(
                    $"project_grounding:pack_used project='{TrimForLog(groundedProject.Title, 120)}' kind={packKind} snippets={cachedPack.Snippets.Count}");
                return;
            }

            _structuredKnowledgeContext = BuildProjectGrounding(groundedProject, packKind);
            RagTraceLogger.WriteLine(
                $"project_grounding:selected project='{TrimForLog(groundedProject.Title, 120)}' scope={plannerDecision.Scope} target='{TrimForLog(plannerDecision.Target, 120)}' pack={packKind}");
            UpdateSystemPromptWithContext();

            if (ShouldRetrieveProjectSnippets(plannerDecision, groundedProject, packKind))
            {
                await RefreshRetrievedKnowledgeSnippetsAsync(
                    plannerDecision.KnowledgeQuery,
                    groundedProject.SourceDocumentIds,
                    cancellationToken);
            }
            else
            {
                RagTraceLogger.WriteLine("project_grounding:structured_only=true");
                ClearRetrievedKnowledgeSnippets();
            }
        }

        private async Task PrepareExperienceGroundingAsync(string userMessage, CancellationToken cancellationToken)
        {
            var knowledgeBase = await LoadKnowledgeBaseSummaryAsync(cancellationToken);
            var experience = SelectExperienceCard(
                knowledgeBase?.ExperienceCards ?? Array.Empty<HostedKnowledgeBaseExperienceCardDto>(),
                userMessage);
            if (experience == null)
            {
                ClearStructuredKnowledgeContext();
                ClearRetrievedKnowledgeSnippets();
                RagTraceLogger.WriteLine("experience_grounding:missing");
                return;
            }

            ClearRetrievedKnowledgeSnippets();
            _structuredKnowledgeContext = BuildExperienceGrounding(experience);
            RagTraceLogger.WriteLine(
                $"experience_grounding:selected company='{TrimForLog(experience.Company, 100)}' role='{TrimForLog(experience.Role, 100)}' current={experience.IsCurrent}");
            UpdateSystemPromptWithContext();
        }

        private static HostedKnowledgeBaseExperienceCardDto? SelectExperienceCard(
            IReadOnlyList<HostedKnowledgeBaseExperienceCardDto> experiences,
            string userMessage)
        {
            if (experiences.Count == 0)
            {
                return null;
            }

            var tokens = Regex.Matches(NormalizeText(userMessage), @"[a-z0-9]+")
                .Select(match => match.Value)
                .Where(token => token.Length >= 3 || token is "ui" or "qa")
                .Distinct(StringComparer.Ordinal)
                .Where(token => token is not "experience" and not "role" and not "company" and not "current" and not "your" and not "have")
                .ToArray();
            return experiences
                .Select(card => new
                {
                    Card = card,
                    Score = (card.IsCurrent ? 4 : 0) + tokens.Count(token => NormalizeText(string.Join(" ",
                        card.Company,
                        card.Role,
                        card.Summary,
                        card.Responsibilities,
                        string.Join(" ", card.Skills))).Contains(token, StringComparison.Ordinal)) * 10
                })
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Card.SortOrder)
                .First().Card;
        }

        private void PrimeKnowledgeBaseSummaryLoad(CancellationToken cancellationToken)
        {
            if (_knowledgeBaseLoader == null)
            {
                return;
            }

            if (HasFreshKnowledgeBaseSummaryCache())
            {
                PrimeInterviewContextPackWarmup();
                return;
            }

            if (_knowledgeBaseSummaryLoadTask == null || _knowledgeBaseSummaryLoadTask.IsCompleted)
            {
                _knowledgeBaseSummaryLoadTask = RefreshKnowledgeBaseSummaryAsync(cancellationToken);
            }
        }

        private bool HasFreshKnowledgeBaseSummaryCache()
        {
            return _knowledgeBaseSummaryCache != null;
        }

        private async Task<HostedKnowledgeBaseSummaryDto?> LoadKnowledgeBaseSummaryAsync(CancellationToken cancellationToken)
        {
            if (_knowledgeBaseLoader == null)
            {
                return _knowledgeBaseSummaryCache;
            }

            if (HasFreshKnowledgeBaseSummaryCache())
            {
                PrimeInterviewContextPackWarmup();
                return _knowledgeBaseSummaryCache;
            }

            if (_knowledgeBaseSummaryLoadTask == null || _knowledgeBaseSummaryLoadTask.IsCompleted)
            {
                _knowledgeBaseSummaryLoadTask = RefreshKnowledgeBaseSummaryAsync(cancellationToken);
            }

            try
            {
                var summary = await _knowledgeBaseSummaryLoadTask;
                PrimeInterviewContextPackWarmup();
                return summary;
            }
            finally
            {
                if (_knowledgeBaseSummaryLoadTask != null && _knowledgeBaseSummaryLoadTask.IsCompleted)
                {
                    _knowledgeBaseSummaryLoadTask = null;
                }
            }
        }

        private async Task<HostedKnowledgeBaseSummaryDto?> RefreshKnowledgeBaseSummaryAsync(CancellationToken cancellationToken)
        {
            try
            {
                var summary = await _knowledgeBaseLoader!(cancellationToken);
                var revision = BuildKnowledgeBaseRevision(summary);
                if (!string.Equals(_knowledgeBaseRevision, revision, StringComparison.Ordinal))
                {
                    _interviewContextPacks.Clear();
                    _knowledgeBaseRevision = revision;
                }
                _knowledgeBaseSummaryCache = summary;
                RagTraceLogger.WriteLine(
                    $"kb_summary:refresh_success status='{_knowledgeBaseSummaryCache?.Status ?? "(null)"}' projects={_knowledgeBaseSummaryCache?.ProjectCards?.Count ?? 0}");
                PrimeInterviewContextPackWarmup();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Hosted KB summary refresh failed: {ex.Message}");
                RagTraceLogger.WriteLine($"kb_summary:refresh_error message='{TrimForLog(ex.Message, 240)}'");
            }

            return _knowledgeBaseSummaryCache;
        }

        private void PrimeInterviewContextPackWarmup()
        {
            if (_knowledgeRetriever == null
                || _knowledgeBaseSummaryCache == null
                || !HasFreshKnowledgeBaseSummaryCache())
            {
                return;
            }

            if (_interviewContextPackWarmupTask == null || _interviewContextPackWarmupTask.IsCompleted)
            {
                _interviewContextPackWarmupTask = WarmInterviewContextPacksAsync(_knowledgeBaseSummaryCache, CancellationToken.None);
            }
        }

        private async Task WarmInterviewContextPacksAsync(
            HostedKnowledgeBaseSummaryDto knowledgeBase,
            CancellationToken cancellationToken)
        {
            if (!HasProfileGrounding(knowledgeBase.ProfileCard)
                && (knowledgeBase.ProjectCards == null || knowledgeBase.ProjectCards.Count == 0))
            {
                return;
            }

            try
            {
                if (HasProfileGrounding(knowledgeBase.ProfileCard))
                {
                    await CacheProfilePackAsync(knowledgeBase.ProfileCard, InterviewPackKind.ProfileGeneral, cancellationToken);
                }

                var projects = knowledgeBase.ProjectCards ?? Array.Empty<HostedKnowledgeBaseProjectCardDto>();
                var primaryProject = projects
                    .OrderByDescending(card => string.Equals(card.ProjectCardId, _activeProjectCardId, StringComparison.Ordinal))
                    .ThenByDescending(card => card.IsRecent)
                    .ThenBy(card => card.SortOrder)
                    .FirstOrDefault();
                if (HasProjectGrounding(primaryProject))
                {
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectOverview, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Interview context pack warmup failed: {ex.Message}");
            }
        }

        private async Task CacheProfilePackAsync(
            HostedKnowledgeBaseProfileCardDto profileCard,
            InterviewPackKind packKind,
            CancellationToken cancellationToken)
        {
            var packKey = BuildProfilePackCacheKey(packKind);
            if (TryGetCachedInterviewContextPack(packKey, out _))
            {
                return;
            }

            await Task.Yield();
            _interviewContextPacks[packKey] = new CachedInterviewContextPack
            {
                StructuredContext = BuildProfileGrounding(profileCard, packKind),
                Snippets = Array.Empty<RetrievedContextSnippet>()
            };
        }

        private async Task CacheProjectPackAsync(
            HostedKnowledgeBaseProjectCardDto projectCard,
            InterviewPackKind packKind,
            CancellationToken cancellationToken)
        {
            var packKey = BuildProjectPackCacheKey(projectCard.ProjectCardId, packKind);
            if (TryGetCachedInterviewContextPack(packKey, out _))
            {
                return;
            }

            IReadOnlyList<RetrievedContextSnippet> snippets = Array.Empty<RetrievedContextSnippet>();
            if (_knowledgeRetriever != null
                && projectCard.SourceDocumentIds.Count > 0
                && (packKind == InterviewPackKind.ProjectArchitecture || packKind == InterviewPackKind.ProjectChallenges))
            {
                try
                {
                    snippets = await _knowledgeRetriever(
                        packKind == InterviewPackKind.ProjectArchitecture
                            ? "project architecture technologies design tradeoffs impact"
                            : "project challenges tradeoffs problem solving impact",
                        projectCard.SourceDocumentIds,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"⚠️ Project pack retrieval warmup failed: {ex.Message}");
                    snippets = Array.Empty<RetrievedContextSnippet>();
                }
            }

            _interviewContextPacks[packKey] = new CachedInterviewContextPack
            {
                StructuredContext = BuildProjectGrounding(projectCard, packKind),
                Snippets = snippets
            };
        }

        private bool TryApplyInterviewContextPack(string packKey, out CachedInterviewContextPack pack)
        {
            if (!TryGetCachedInterviewContextPack(packKey, out pack))
            {
                return false;
            }

            _structuredKnowledgeContext = pack.StructuredContext;
            _retrievedKnowledgeSnippets = pack.Snippets;
            _lastRetrievedDocumentIds = pack.Snippets
                .Select(snippet => snippet.DocumentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            UpdateSystemPromptWithContext();
            return true;
        }

        private bool TryGetCachedInterviewContextPack(string packKey, out CachedInterviewContextPack pack)
        {
            if (_interviewContextPacks.TryGetValue(packKey, out pack!)) return true;
            pack = new CachedInterviewContextPack();
            return false;
        }

        private HostedKnowledgeBaseProjectCardDto? SelectProjectCard(
            IReadOnlyList<HostedKnowledgeBaseProjectCardDto> projectCards,
            ResponsePlan plannerDecision,
            string userMessage)
        {
            if (projectCards == null || projectCards.Count == 0)
            {
                return null;
            }

            if (string.Equals(plannerDecision.Source, "heuristic-alternate-project", StringComparison.Ordinal))
            {
                return projectCards
                    .Where(card => string.IsNullOrWhiteSpace(_activeProjectCardId)
                        || !string.Equals(card.ProjectCardId, _activeProjectCardId, StringComparison.Ordinal))
                    .OrderByDescending(card => card.IsRecent)
                    .ThenBy(card => card.SortOrder)
                    .FirstOrDefault();
            }

            var requestedTarget = NormalizeText(plannerDecision.Target);
            if (!string.IsNullOrWhiteSpace(requestedTarget))
            {
                return projectCards.FirstOrDefault(card =>
                {
                    var title = NormalizeText(card.Title);
                    var slug = NormalizeText(card.Slug);
                    return title.Contains(requestedTarget, StringComparison.Ordinal)
                        || requestedTarget.Contains(title, StringComparison.Ordinal)
                        || slug.Contains(requestedTarget, StringComparison.Ordinal);
                });
            }

            if (plannerDecision.Scope == RetrievalScope.ActiveProject
                && !string.IsNullOrWhiteSpace(_activeProjectCardId))
            {
                var activeProject = projectCards.FirstOrDefault(card =>
                    string.Equals(card.ProjectCardId, _activeProjectCardId, StringComparison.Ordinal));
                if (activeProject != null)
                {
                    return activeProject;
                }
            }

            var scoredProjects = projectCards
                .Select(card => new
                {
                    Card = card,
                    Score = ScoreProjectCard(card, plannerDecision.Target, plannerDecision.KnowledgeQuery, userMessage)
                })
                .OrderByDescending(item => item.Score)
                .ThenByDescending(item => item.Card.IsRecent)
                .ThenBy(item => item.Card.SortOrder)
                .ToArray();

            if (scoredProjects.Length > 0 && scoredProjects[0].Score > 0)
            {
                return scoredProjects[0].Card;
            }

            return projectCards
                .OrderByDescending(card => card.IsRecent)
                .ThenBy(card => card.SortOrder)
                .FirstOrDefault();
        }

        private static int ScoreProjectCard(
            HostedKnowledgeBaseProjectCardDto projectCard,
            string target,
            string knowledgeQuery,
            string userMessage)
        {
            var score = 0;
            var title = NormalizeText(projectCard.Title);
            var slug = NormalizeText(projectCard.Slug);
            var haystack = NormalizeText(string.Join(
                " ",
                new[]
                {
                    projectCard.Title,
                    projectCard.Slug,
                    projectCard.Role,
                    projectCard.Summary,
                    projectCard.Architecture,
                    projectCard.Challenges,
                    projectCard.Impact,
                    string.Join(" ", projectCard.Stack ?? Array.Empty<string>())
                }));

            var targetNormalized = NormalizeText(target);
            if (!string.IsNullOrWhiteSpace(targetNormalized))
            {
                if (title.Contains(targetNormalized, StringComparison.Ordinal)
                    || slug.Contains(targetNormalized, StringComparison.Ordinal))
                {
                    score += 100;
                }
                else if (haystack.Contains(targetNormalized, StringComparison.Ordinal))
                {
                    score += 35;
                }
            }

            foreach (var token in ExtractMeaningfulTokens($"{knowledgeQuery} {userMessage}"))
            {
                if (title.Contains(token, StringComparison.Ordinal) || slug.Contains(token, StringComparison.Ordinal))
                {
                    score += 8;
                }
                else if (haystack.Contains(token, StringComparison.Ordinal))
                {
                    score += 2;
                }
            }

            if (projectCard.IsRecent)
            {
                score += 5;
            }

            return score;
        }

        private static IEnumerable<string> ExtractMeaningfulTokens(string value)
        {
            var tokens = NormalizeText(value)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length >= 3)
                .Distinct(StringComparer.Ordinal);

            foreach (var token in tokens)
            {
                yield return token;
            }
        }

        private static string NormalizeText(string? value)
        {
            return Regex.Replace((value ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", " ").Trim();
        }

        private static bool HasProfileGrounding(HostedKnowledgeBaseProfileCardDto? profileCard)
        {
            if (profileCard == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(profileCard.ShortIntro)
                || !string.IsNullOrWhiteSpace(profileCard.CandidateInfo)
                || !string.IsNullOrWhiteSpace(profileCard.ResumeText)
                || !string.IsNullOrWhiteSpace(profileCard.FullName)
                || !string.IsNullOrWhiteSpace(profileCard.CurrentRole)
                || profileCard.Skills.Count > 0
                || profileCard.Strengths.Count > 0
                || profileCard.Domains.Count > 0;
        }

        private static bool HasProfileWorkEvidence(HostedKnowledgeBaseProfileCardDto? profileCard)
            => profileCard != null
                && (profileCard.YearsOfExperience > 0
                    || !string.IsNullOrWhiteSpace(profileCard.CurrentRole)
                    || !string.IsNullOrWhiteSpace(profileCard.CandidateInfo)
                    || !string.IsNullOrWhiteSpace(profileCard.ResumeText));

        private static bool HasProjectGrounding(HostedKnowledgeBaseProjectCardDto? projectCard)
        {
            if (projectCard == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(projectCard.Title)
                || !string.IsNullOrWhiteSpace(projectCard.Summary)
                || !string.IsNullOrWhiteSpace(projectCard.Architecture)
                || !string.IsNullOrWhiteSpace(projectCard.Impact)
                || projectCard.Stack.Count > 0;
        }

        private static bool HasRichProjectGrounding(HostedKnowledgeBaseProjectCardDto projectCard)
        {
            return !string.IsNullOrWhiteSpace(projectCard.Title)
                && !string.IsNullOrWhiteSpace(projectCard.Summary)
                && !string.IsNullOrWhiteSpace(projectCard.Architecture)
                && !string.IsNullOrWhiteSpace(projectCard.Impact)
                && projectCard.Stack.Count > 0;
        }

        private static bool ShouldRetrieveProjectSnippets(
            ResponsePlan plannerDecision,
            HostedKnowledgeBaseProjectCardDto projectCard,
            InterviewPackKind packKind)
        {
            if (projectCard.SourceDocumentIds.Count == 0)
            {
                return false;
            }

            if (packKind == InterviewPackKind.ProjectArchitecture)
            {
                return string.IsNullOrWhiteSpace(projectCard.Architecture);
            }

            if (packKind == InterviewPackKind.ProjectChallenges)
            {
                return string.IsNullOrWhiteSpace(projectCard.Challenges);
            }

            if (packKind == InterviewPackKind.ProjectStack)
            {
                return projectCard.Stack.Count == 0;
            }

            if (packKind == InterviewPackKind.ProjectImpact)
            {
                return string.IsNullOrWhiteSpace(projectCard.Impact);
            }

            if (!HasRichProjectGrounding(projectCard))
            {
                return true;
            }

            var query = NormalizeText($"{plannerDecision.KnowledgeQuery} {plannerDecision.Target}");
            return ContainsAnyToken(
                query,
                "api",
                "authentication",
                "auth",
                "code",
                "database",
                "deployment",
                "implementation",
                "implemented",
                "deep dive",
                "details",
                "specific",
                "exact",
                "performance",
                "scalability",
                "security",
                "snippet",
                "tradeoff",
                "flow",
                "internals");
        }

        private static bool ContainsAnyToken(string value, params string[] terms)
        {
            return terms.Any(term => value.Contains(NormalizeText(term), StringComparison.Ordinal));
        }

        private InterviewPackKind DetermineProfilePackKind(ResponsePlan plannerDecision, string userMessage)
        {
            var query = NormalizeText($"{plannerDecision.KnowledgeQuery} {userMessage}");
            if (ContainsAnyToken(query, "strength", "strengths", "strong", "skills", "why should we hire you"))
            {
                return InterviewPackKind.ProfileStrengths;
            }

            if (ContainsAnyToken(query, "current role", "current position", "responsibilities", "responsibility"))
            {
                return InterviewPackKind.ProfileRole;
            }

            if (ContainsAnyToken(query, "introduce yourself", "tell me about yourself", "background", "experience", "resume"))
            {
                return InterviewPackKind.ProfileIntro;
            }

            return InterviewPackKind.ProfileGeneral;
        }

        private InterviewPackKind DetermineProjectPackKind(ResponsePlan plannerDecision, string userMessage)
        {
            var query = NormalizeText($"{plannerDecision.KnowledgeQuery} {plannerDecision.Target} {userMessage}");
            if (ContainsAnyToken(query, "architecture", "design", "system design", "performance", "security", "scalability"))
            {
                return InterviewPackKind.ProjectArchitecture;
            }

            if (ContainsAnyToken(query, "challenge", "challenges", "tradeoff", "tradeoffs", "problem", "difficult"))
            {
                return InterviewPackKind.ProjectChallenges;
            }

            if (ContainsAnyToken(query, "stack", "tech stack", "technology", "technologies", "tools", "framework"))
            {
                return InterviewPackKind.ProjectStack;
            }

            if (ContainsAnyToken(query, "impact", "outcome", "result", "results", "achievement", "achievements"))
            {
                return InterviewPackKind.ProjectImpact;
            }

            return InterviewPackKind.ProjectOverview;
        }

        private string BuildProfilePackCacheKey(InterviewPackKind packKind)
        {
            return $"{_knowledgeBaseRevision}:profile:{packKind}";
        }

        private string BuildProjectPackCacheKey(string projectCardId, InterviewPackKind packKind)
        {
            return $"{_knowledgeBaseRevision}:project:{projectCardId}:{packKind}";
        }

        private static string BuildKnowledgeBaseRevision(HostedKnowledgeBaseSummaryDto? summary)
            => summary == null ? string.Empty : $"{summary.KnowledgeBaseId}:{summary.LastProcessedAtUtc?.Ticks ?? 0}";

        private static string BuildProfileGrounding(
            HostedKnowledgeBaseProfileCardDto profileCard,
            InterviewPackKind packKind)
        {
            var lines = new List<string>
            {
                "Use only this candidate profile for personal/background questions. If a detail is missing here, say so instead of inventing it."
            };

            AppendGroundingLine(lines, "Full name", profileCard.FullName);

            switch (packKind)
            {
                case InterviewPackKind.ProfileIntro:
                    AppendGroundingLine(lines, "Short intro", profileCard.ShortIntro);
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingList(lines, "Skills", profileCard.Skills.Take(6).ToArray());
                    break;

                case InterviewPackKind.ProfileStrengths:
                    AppendGroundingList(lines, "Strengths", profileCard.Strengths);
                    AppendGroundingList(lines, "Skills", profileCard.Skills);
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingLine(lines, "Candidate information", TruncateMessageStatic(
                        string.IsNullOrWhiteSpace(profileCard.CandidateInfo) ? profileCard.ResumeText : profileCard.CandidateInfo,
                        600));
                    break;

                case InterviewPackKind.ProfileRole:
                    AppendGroundingLine(lines, "Current role", profileCard.CurrentRole);
                    if (profileCard.YearsOfExperience > 0)
                    {
                        lines.Add($"Years of experience: {profileCard.YearsOfExperience}");
                    }
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingList(lines, "Skills", profileCard.Skills.Take(8).ToArray());
                    break;

                default:
                    AppendGroundingLine(lines, "Short intro", profileCard.ShortIntro);
                    AppendGroundingList(lines, "Strengths", profileCard.Strengths);
                    AppendGroundingList(lines, "Skills", profileCard.Skills);
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingLine(lines, "Candidate information", TruncateMessageStatic(
                        string.IsNullOrWhiteSpace(profileCard.CandidateInfo) ? profileCard.ResumeText : profileCard.CandidateInfo,
                        1200));
                    break;
            }

            return string.Join("\n", lines);
        }

        private static string BuildExperienceGrounding(HostedKnowledgeBaseExperienceCardDto experience)
        {
            var lines = new List<string>
            {
                experience.IsCurrent
                    ? "Answer from this current role only. Do not mix responsibilities from previous companies."
                    : "This past role is relevant to the question. Answer from this role only and do not present it as current work."
            };
            AppendGroundingLine(lines, "Company", experience.Company);
            AppendGroundingLine(lines, "Role", experience.Role);
            AppendGroundingLine(lines, "Period", string.Join(" – ", new[]
            {
                experience.StartDate,
                experience.IsCurrent ? "Present" : experience.EndDate
            }.Where(value => !string.IsNullOrWhiteSpace(value))));
            AppendGroundingLine(lines, "Summary", experience.Summary);
            AppendGroundingLine(lines, "Responsibilities and achievements", experience.Responsibilities);
            AppendGroundingList(lines, "Skills and tools", experience.Skills);
            return string.Join("\n", lines);
        }

        private static string BuildProjectGrounding(
            HostedKnowledgeBaseProjectCardDto projectCard,
            InterviewPackKind packKind)
        {
            var lines = new List<string>
            {
                "Use only this project for the current answer. If the requested detail is missing, say so briefly instead of inventing it."
            };

            AppendGroundingLine(lines, "Project", projectCard.Title);
            AppendGroundingLine(lines, "Role", projectCard.Role);

            switch (packKind)
            {
                case InterviewPackKind.ProjectArchitecture:
                    AppendGroundingLine(lines, "Summary", projectCard.Summary);
                    AppendGroundingList(lines, "Stack", projectCard.Stack);
                    AppendGroundingLine(lines, "Architecture", projectCard.Architecture);
                    AppendGroundingLine(lines, "Impact", projectCard.Impact);
                    break;

                case InterviewPackKind.ProjectChallenges:
                    AppendGroundingLine(lines, "Summary", projectCard.Summary);
                    AppendGroundingLine(lines, "Challenges", projectCard.Challenges);
                    AppendGroundingLine(lines, "Impact", projectCard.Impact);
                    break;

                case InterviewPackKind.ProjectStack:
                    AppendGroundingLine(lines, "Summary", projectCard.Summary);
                    AppendGroundingList(lines, "Stack", projectCard.Stack);
                    AppendGroundingLine(lines, "Architecture", projectCard.Architecture);
                    break;

                case InterviewPackKind.ProjectImpact:
                    AppendGroundingLine(lines, "Summary", projectCard.Summary);
                    AppendGroundingLine(lines, "Impact", projectCard.Impact);
                    AppendGroundingLine(lines, "Challenges", projectCard.Challenges);
                    break;

                default:
                    AppendGroundingLine(lines, "Summary", projectCard.Summary);
                    AppendGroundingList(lines, "Stack", projectCard.Stack);
                    AppendGroundingLine(lines, "Architecture", projectCard.Architecture);
                    AppendGroundingLine(lines, "Challenges", projectCard.Challenges);
                    AppendGroundingLine(lines, "Impact", projectCard.Impact);
                    break;
            }

            return string.Join("\n", lines);
        }

        private static string BuildProfileProjectFallbackGrounding(HostedKnowledgeBaseProfileCardDto profileCard)
        {
            var lines = new List<string>
            {
                "No separate project card is available for this request. Switch away from the previously discussed project and describe a different real work or resume project only if the profile evidence below supports it. Never say the conversation is locked to one project, and never invent missing details."
            };
            AppendGroundingLine(lines, "Current role", profileCard.CurrentRole);
            if (profileCard.YearsOfExperience > 0)
            {
                lines.Add($"Years of experience: {profileCard.YearsOfExperience}");
            }
            AppendGroundingList(lines, "Domains", profileCard.Domains);
            AppendGroundingList(lines, "Skills", profileCard.Skills);
            AppendGroundingLine(lines, "Resume details", TruncateMessageStatic(profileCard.ResumeText, 1600));
            return string.Join("\n", lines);
        }

        private static string BuildGenericProjectFallbackGrounding()
            => "No grounded candidate project or work-project evidence is available. Give a concise generic fresher project-answer template, clearly label it as an example the candidate must adapt, and do not claim it as real experience.";

        private static void AppendGroundingLine(ICollection<string> lines, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add($"{label}: {value.Trim()}");
            }
        }

        private static void AppendGroundingList(ICollection<string> lines, string label, IReadOnlyList<string>? values)
        {
            if (values == null || values.Count == 0)
            {
                return;
            }

            var cleaned = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            if (cleaned.Length > 0)
            {
                lines.Add($"{label}: {string.Join(", ", cleaned)}");
            }
        }

        private static string TruncateMessageStatic(string? text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Length <= maxChars ? text : text.Substring(0, maxChars) + "...";
        }

        private void ClearStructuredKnowledgeContext()
        {
            if (string.IsNullOrWhiteSpace(_structuredKnowledgeContext))
            {
                return;
            }

            _structuredKnowledgeContext = string.Empty;
            RagTraceLogger.WriteLine("structured_grounding:clear");
            UpdateSystemPromptWithContext();
        }

        private static string FormatDocumentIds(IReadOnlyList<string>? documentIds)
        {
            if (documentIds == null || documentIds.Count == 0)
            {
                return "(none)";
            }

            return string.Join(", ", documentIds.Where(id => !string.IsNullOrWhiteSpace(id)).Take(6));
        }

        private static string TrimForLog(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var normalized = Regex.Replace(value, "\\s+", " ").Trim();
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength) + "...";
        }

        private static string FormatKeyUsageForLog(int currentKeyIndex, int totalKeys)
        {
            return totalKeys <= 0
                ? "managed auth"
                : $"Key #{Math.Min(currentKeyIndex + 1, totalKeys)}/{totalKeys}";
        }

        private async Task RefreshRetrievedKnowledgeSnippetsAsync(
            string retrievalQuery,
            IReadOnlyList<string>? preferredDocumentIds,
            CancellationToken cancellationToken)
        {
            var previousSnippets = _retrievedKnowledgeSnippets;
            var requestedDocumentIds = preferredDocumentIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal)
                ?? new HashSet<string>(StringComparer.Ordinal);
            var previousDocumentIds = previousSnippets
                .Select(snippet => snippet.DocumentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            var canReusePreviousSnippets = requestedDocumentIds.Count > 0
                && requestedDocumentIds.SetEquals(previousDocumentIds);
            RagTraceLogger.WriteLine(
                $"retrieval:start query='{TrimForLog(retrievalQuery, 240)}' preferred_docs={FormatDocumentIds(preferredDocumentIds)} previous_snippet_count={previousSnippets.Count}");
            LiveRequestTrace.Current?.Mark("retrieval_started");
            var retrievedSnippets = _knowledgeRetriever == null
                ? Array.Empty<RetrievedContextSnippet>()
                : await _knowledgeRetriever(retrievalQuery, preferredDocumentIds, cancellationToken);
            LiveRequestTrace.Current?.Mark("retrieval_finished");
            if (requestedDocumentIds.Count > 0)
            {
                retrievedSnippets = retrievedSnippets
                    .Where(snippet => requestedDocumentIds.Contains(snippet.DocumentId))
                    .ToArray();
            }

            if (retrievedSnippets.Count > 0)
            {
                _retrievedKnowledgeSnippets = retrievedSnippets;
                _lastRetrievedDocumentIds = retrievedSnippets
                    .Select(snippet => snippet.DocumentId)
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                RagTraceLogger.WriteLine(
                    $"retrieval:success count={retrievedSnippets.Count} docs={string.Join(", ", retrievedSnippets.Select(snippet => TrimForLog(snippet.DocumentTitle, 80)))}");
            }
            else if (canReusePreviousSnippets && previousSnippets.Count > 0)
            {
                _retrievedKnowledgeSnippets = previousSnippets;
                Log.WriteLine("✓ Preserving previous retrieved knowledge snippets after empty retrieval");
                RagTraceLogger.WriteLine("retrieval:empty preserving_previous_snippets=true");
            }
            else
            {
                _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
                _lastRetrievedDocumentIds = Array.Empty<string>();
                RagTraceLogger.WriteLine("retrieval:empty preserving_previous_snippets=false");
            }

            UpdateSystemPromptWithContext();
        }

        private void ClearRetrievedKnowledgeSnippets()
        {
            if (_retrievedKnowledgeSnippets.Count == 0 && _lastRetrievedDocumentIds.Count == 0)
            {
                return;
            }

            _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
            _lastRetrievedDocumentIds = Array.Empty<string>();
            RagTraceLogger.WriteLine("retrieval:clear");
            UpdateSystemPromptWithContext();
        }

        private string? BuildRetrievalMissResponse(ResponsePlan plannerDecision)
        {
            if (plannerDecision.Type == ResponsePlanType.Direct)
            {
                return null;
            }

            if (plannerDecision.Type == ResponsePlanType.Profile)
            {
                return string.IsNullOrWhiteSpace(_structuredKnowledgeContext)
                    ? "I couldn't find your profile details in the selected knowledge base. Add or review your profile section so I can answer personal interview questions accurately."
                    : null;
            }

            if (plannerDecision.Type == ResponsePlanType.Project)
            {
                return string.IsNullOrWhiteSpace(_structuredKnowledgeContext)
                    ? "I couldn't find a grounded project match in the selected knowledge base. Add a project section, mark the right project as recent, or ask with the exact project name."
                    : null;
            }

            if (plannerDecision.Type == ResponsePlanType.Experience)
            {
                return string.IsNullOrWhiteSpace(_structuredKnowledgeContext)
                    ? "I couldn't find a matching work experience. Add the company and role in your Experience section so I can answer accurately."
                    : null;
            }

            if (_retrievedKnowledgeSnippets.Count > 0)
            {
                return null;
            }

            return "I couldn't find relevant information for that in your selected knowledge base or context. Please mention the exact project or topic, or verify that the document is indexed and interview-usable.";
        }


        private (string fullResponse, string summary, bool hasCode) ParseAIResponse(string response)
        {
            // Check if response contains code blocks
            bool hasCode = response.Contains("```");

            // Try to extract structured response
            var summaryMatch = Regex.Match(response, @"SUMMARY:\s*(.+?)(?:\n|$)", RegexOptions.IgnoreCase);
            
            string summary;
            if (summaryMatch.Success)
            {
                summary = summaryMatch.Groups[1].Value.Trim();
                // Remove the SUMMARY line from the response
                response = Regex.Replace(response, @"SUMMARY:\s*.+?(?:\n|$)", "", RegexOptions.IgnoreCase).Trim();
            }
            else
            {
                // Auto-generate summary (first 100 characters)
                summary = response.Length > 100 ? response.Substring(0, 100) + "..." : response;
            }

            return (response, summary, hasCode);
        }

        private enum ResponsePlanType
        {
            Direct,
            Profile,
            Project,
            Experience,
            Retrieve
        }

        private enum RetrievalScope
        {
            Global,
            PreviousDocuments,
            ActiveProject
        }

        private enum InterviewPackKind
        {
            ProfileGeneral,
            ProfileIntro,
            ProfileStrengths,
            ProfileRole,
            ProjectOverview,
            ProjectArchitecture,
            ProjectChallenges,
            ProjectStack,
            ProjectImpact
        }

        private sealed class ResponsePlan
        {
            public ResponsePlanType Type { get; init; }
            public string DirectAnswer { get; init; } = string.Empty;
            public string KnowledgeQuery { get; init; } = string.Empty;
            public string Source { get; init; } = string.Empty;
            public RetrievalScope Scope { get; init; }
            public string Target { get; init; } = string.Empty;
            public double Confidence { get; init; }

            public static ResponsePlan Direct(string directAnswer, double confidence)
            {
                return new ResponsePlan
                {
                    Type = ResponsePlanType.Direct,
                    DirectAnswer = directAnswer ?? string.Empty,
                    Confidence = confidence,
                    Source = "deterministic"
                };
            }

            public static ResponsePlan Profile(
                string knowledgeQuery,
                string target,
                RetrievalScope scope,
                double confidence,
                string source = "deterministic")
            {
                return new ResponsePlan
                {
                    Type = ResponsePlanType.Profile,
                    KnowledgeQuery = knowledgeQuery ?? string.Empty,
                    Target = target ?? string.Empty,
                    Scope = scope,
                    Confidence = confidence,
                    Source = source
                };
            }

            public static ResponsePlan Project(
                string knowledgeQuery,
                string target,
                RetrievalScope scope,
                double confidence,
                string source = "deterministic")
            {
                return new ResponsePlan
                {
                    Type = ResponsePlanType.Project,
                    KnowledgeQuery = knowledgeQuery ?? string.Empty,
                    Target = target ?? string.Empty,
                    Scope = scope,
                    Confidence = confidence,
                    Source = source
                };
            }

            public static ResponsePlan Experience(string knowledgeQuery, double confidence, string source)
            {
                return new ResponsePlan
                {
                    Type = ResponsePlanType.Experience,
                    KnowledgeQuery = knowledgeQuery ?? string.Empty,
                    Confidence = confidence,
                    Scope = RetrievalScope.Global,
                    Source = source
                };
            }

            public static ResponsePlan Retrieve(
                string knowledgeQuery,
                string source,
                RetrievalScope scope = RetrievalScope.Global,
                string target = "",
                double confidence = 0d)
            {
                return new ResponsePlan
                {
                    Type = ResponsePlanType.Retrieve,
                    KnowledgeQuery = knowledgeQuery ?? string.Empty,
                    Source = source ?? string.Empty,
                    Scope = scope,
                    Target = target ?? string.Empty,
                    Confidence = confidence
                };
            }
        }

        private sealed class CachedInterviewContextPack
        {
            public string StructuredContext { get; init; } = string.Empty;
            public IReadOnlyList<RetrievedContextSnippet> Snippets { get; init; } = Array.Empty<RetrievedContextSnippet>();
        }

        // ═══════════════════════════════════════════════════════════════
        // RETRY LOGIC
        // ═══════════════════════════════════════════════════════════════

        // ═══════════════════════════════════════════════════════════════
        // UPDATED: SendWithRetryStreamAsync with rotation support
        // ═══════════════════════════════════════════════════════════════
        /// <summary>
        /// Send message with automatic retry and rotation (STREAMING version)
        /// NOTE: Does NOT reset model on each call - only on new conversation!
        /// </summary>
        private async Task<(string response, string error)> SendWithRetryStreamAsync(
            List<ConversationMessage> context, 
            Action<string> onChunkReceived,
            CancellationToken cancellationToken,
            string? imageBase64 = null,
            Action? onRetryCleanup = null)
        {
            int maxAttempts = 5;
            int currentAttempt = 0;
            bool hasTriedModelSwitch = false;
            bool hasTriedKeySwitch = false;

            var currentModel = _rotationManager?.GetCurrentModel(_currentProvider) ?? "";
            var totalKeys = _rotationManager?.GetTotalKeyCount(_currentProvider) ?? 0;

            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("SEND WITH RETRY (STREAMING)");
            Log.WriteLine($"Provider: {_currentProvider}");
            Log.WriteLine($"Current model: {currentModel}");
            Log.WriteLine($"Total Keys: {totalKeys}");
            Log.WriteLine($"Auto-switch Keys: {_rotationManager?.IsAutoSwitchKeysEnabled}");
            Log.WriteLine($"Auto-switch Models: {_rotationManager?.IsAutoSwitchModelsEnabled}");
            Log.WriteLine("NOTE: Model persists within this conversation");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            while (currentAttempt < maxAttempts)
            {
                currentAttempt++;
                
                try
                {
                    Log.WriteLine($"───────────────────────────────────────────────────");
                    Log.WriteLine($"Attempt {currentAttempt}/{maxAttempts}");
                    
                    var currentKeyIndex = _rotationManager?.GetCurrentKeyIndex(_currentProvider) ?? 0;
                    currentModel = _rotationManager?.GetCurrentModel(_currentProvider) ?? "";
                    
                    Log.WriteLine($"Using: {FormatKeyUsageForLog(currentKeyIndex, totalKeys)}, Model: {currentModel}");
                    LiveRequestTrace.Current?.Mark("provider_request_started");
                    
                    var response = await _aiService.SendMessageStreamAsync(context, onChunkReceived, cancellationToken, imageBase64);

                    if (response.StartsWith("Error:"))
                    {
                        var errorMsg = response.Substring(6).Trim();
                        Log.WriteLine($"⚠️ API Error: {errorMsg}");

                        if (_rotationManager != null)
                        {
                            bool is429 = _rotationManager.Is429Error(errorMsg);
                            bool isRetryable = _rotationManager.IsRetryableError(errorMsg);
                            var lowerError = errorMsg.ToLower();
                            bool isAuthError = lowerError.Contains("401") || 
                                            lowerError.Contains("403") ||
                                            lowerError.Contains("invalid") && lowerError.Contains("key");

                            // ═══════════════════════════════════════════════════════════════
                            // CASE 1: 429 Rate Limit Error → Switch Key → Switch Model
                            // ═══════════════════════════════════════════════════════════════
                            if (is429)
                            {
                                Log.WriteLine("🚫 429 Error Detected (rate limit)");
                                Log.WriteLine("   Strategy: Try all keys → Try models (persist for conversation)");
                                var currentKeyIdx = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                _rotationManager.MarkKeyAsRateLimited(_currentProvider, currentKeyIdx);

                                var availableKeys = _rotationManager.GetAvailableKeyCount(_currentProvider);
                                var totalKeysCount = _rotationManager.GetTotalKeyCount(_currentProvider);
                                
                                // Try next key if available
                                if (_rotationManager.IsAutoSwitchKeysEnabled &&
                                    availableKeys > 0)
                                {
                                    ResetStreamingAttempt(onRetryCleanup);
                                    var oldKeyIndex = currentKeyIdx;
                                    var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                    
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                    
                                    var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                    
                                    Log.WriteLine($"   ✓ Switched from Key #{oldKeyIndex + 1} to Key #{newKeyIndex + 1}");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1}/{totalKeysCount} (rate limit)");
                                    
                                    await Task.Delay(1000, cancellationToken);
                                    continue;
                                }
                                // All keys exhausted - try different model (persists for conversation)
                                else if (!hasTriedModelSwitch && 
                                        _rotationManager.IsAutoSwitchModelsEnabled &&
                                        _rotationManager.HasMultipleModels(_currentProvider))
                                {
                                    ResetStreamingAttempt(onRetryCleanup);
                                    Log.WriteLine("   All keys exhausted - trying different model");
                                    Log.WriteLine("   → Model will persist for rest of this conversation");
                                    
                                    var oldModel = _rotationManager.GetCurrentModel(_currentProvider);
                                    var newModel = _rotationManager.GetNextModel(_currentProvider);
                                    
                                    // Reset to first key with new model
                                    _rotationManager.ClearRateLimitedKeys(_currentProvider);
                                    _rotationManager.ResetKeyRotation(_currentProvider);
                                    var firstKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, firstKey, newModel);
                                    hasTriedModelSwitch = true;
                                    
                                    Log.WriteLine($"   ✓ Switched to model: {newModel} with Key #1");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched model: {oldModel} → {newModel} (all keys rate limited)");
                                    
                                    await Task.Delay(1000, cancellationToken);
                                    continue;
                                }
                                else
                                {
                                    // All keys AND models exhausted
                                    Log.WriteLine("   ✗ All API keys and models exhausted");
                                    
                                    var errorMessage = $"⚠️ **All API keys rate limited**\n\n" +
                                                    $"All {totalKeysCount} API key(s) have hit rate limits.\n\n" +
                                                    (hasTriedModelSwitch ? "Also tried alternative models.\n\n" : "") +
                                                    $"Please wait a few minutes or add more API keys in Settings.";
                                    
                                    return ("", errorMessage);
                                }
                            }
                            // ═══════════════════════════════════════════════════════════════
                            // CASE 2: Retryable Error → Retry → Switch Model → Switch Key
                            // ═══════════════════════════════════════════════════════════════
                            else if (isRetryable)
                            {
                                Log.WriteLine("🔄 Retryable Error Detected (timeout/server error)");
                                Log.WriteLine("   Strategy: Retry once → Switch model → Switch key");
                                
                                if (currentAttempt == 1)
                                {
                                    // First retry - same key and model
                                    Log.WriteLine("   Retry 1: Same key/model after 2s delay");
                                    ResetStreamingAttempt(onRetryCleanup);
                                    await Task.Delay(2000, cancellationToken);
                                    continue;
                                }
                                else if (!hasTriedModelSwitch && 
                                        _rotationManager.IsAutoSwitchModelsEnabled &&
                                        _rotationManager.HasMultipleModels(_currentProvider))
                                {
                                    ResetStreamingAttempt(onRetryCleanup);
                                    // Second retry - switch model (persists for conversation)
                                    Log.WriteLine("   Retry 2: Switching model");
                                    Log.WriteLine("   → Model will persist for rest of this conversation");
                                    
                                    var oldModel = _rotationManager.GetCurrentModel(_currentProvider);
                                    var newModel = _rotationManager.GetNextModel(_currentProvider);
                                    var currentKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, currentKey, newModel);
                                    hasTriedModelSwitch = true;
                                    
                                    Log.WriteLine($"   ✓ Switched to model: {newModel}");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched model: {oldModel} → {newModel} (timeout)");
                                    
                                    await Task.Delay(1000, cancellationToken);
                                    continue;
                                }
                                else if (!hasTriedKeySwitch &&
                                        _rotationManager.IsAutoSwitchKeysEnabled &&
                                        _rotationManager.GetTotalKeyCount(_currentProvider) > 1)
                                {
                                    ResetStreamingAttempt(onRetryCleanup);
                                    // Third retry - switch key
                                    Log.WriteLine("   Retry 3: Switching key");
                                    
                                    var oldKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                    var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                    
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                    hasTriedKeySwitch = true;
                                    
                                    var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                    var totalKeysCount = _rotationManager.GetTotalKeyCount(_currentProvider);
                                    
                                    Log.WriteLine($"   ✓ Switched from Key #{oldKeyIndex + 1} to Key #{newKeyIndex + 1}");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1}/{totalKeysCount}");
                                    
                                    await Task.Delay(1000, cancellationToken);
                                    continue;
                                }
                                else
                                {
                                    Log.WriteLine("   ✗ All retry strategies exhausted");
                                    
                                    var errorMessage = $"⚠️ **Request failed after retries**\n\n" +
                                                    $"Error: {errorMsg}\n\n" +
                                                    $"Tried: {currentAttempt} attempts" +
                                                    (hasTriedModelSwitch ? ", model switch" : "") +
                                                    (hasTriedKeySwitch ? ", key switch" : "");
                                    
                                    return ("", errorMessage);
                                }
                            }
                            // ═══════════════════════════════════════════════════════════════
                            // CASE 3: Auth Error → Mark key failed → Switch key
                            // ═══════════════════════════════════════════════════════════════
                            else if (isAuthError)
                            {
                                Log.WriteLine("🚫 Authentication Error - Key is invalid");
                                
                                var currentKeyIdx = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                _rotationManager.MarkKeyAsFailed(_currentProvider, currentKeyIdx);
                                
                                Log.WriteLine($"   Marked Key #{currentKeyIdx + 1} as failed");
                                
                                if (_rotationManager.GetRecoverableKeyCount(_currentProvider) > 0)
                                {
                                    ResetStreamingAttempt(onRetryCleanup);
                                    var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                    
                                    var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                    Log.WriteLine($"   Switched to Key #{newKeyIndex + 1}");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1} (invalid key)");
                                    
                                    await Task.Delay(500, cancellationToken);
                                    continue;
                                }
                                else
                                {
                                    return ("", $"⚠️ **Invalid API Key**\n\n{errorMsg}\n\nPlease check your API key in Settings.");
                                }
                            }
                            // ═══════════════════════════════════════════════════════════════
                            // CASE 4: Non-retryable Error → Return immediately
                            // ═══════════════════════════════════════════════════════════════
                            else
                            {
                                Log.WriteLine($"❌ Non-retryable error: {errorMsg}");
                                return ("", errorMsg);
                            }
                        }
                        else
                        {
                            // No rotation manager - use old retry logic
                            if (IsRetryableError(errorMsg) && currentAttempt < maxAttempts)
                            {
                                var delay = currentAttempt * 2000;
                                Log.WriteLine($"⚠️ Retryable error: {errorMsg}");
                                Log.WriteLine($"Waiting {delay}ms before retry...");
                                ResetStreamingAttempt(onRetryCleanup);
                                await Task.Delay(delay, cancellationToken);
                                continue;
                            }

                            return ("", errorMsg);
                        }
                    }
                    else
                    {
                        // Success!
                        Log.WriteLine($"✓ Request successful");
                        Log.WriteLine($"✓ Conversation will continue with model: {currentModel}");
                        Log.WriteLine("═══════════════════════════════════════════════════════");
                        return (response, "");
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.WriteLine("❌ Request cancelled by user");
                    return ("", "Cancelled");
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"✗ Exception on attempt {currentAttempt}: {ex.Message}");

                    if (currentAttempt < maxAttempts)
                    {
                        var delay = currentAttempt * 1000;
                        Log.WriteLine($"Waiting {delay}ms before retry...");
                        ResetStreamingAttempt(onRetryCleanup);
                        
                        try
                        {
                            await Task.Delay(delay, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            Log.WriteLine("❌ Retry cancelled");
                            return ("", "Cancelled");
                        }
                    }
                    else
                    {
                        var errorMessage = $"⚠️ **Exception after {maxAttempts} attempts**\n\n" +
                                        $"Error: {ex.Message}\n\n" +
                                        $"Check debug logs for details.";
                        return ("", errorMessage);
                    }
                }
            }

            Log.WriteLine($"✗ Failed after {maxAttempts} attempts");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            return ("", $"Failed to get response from AI after {maxAttempts} attempts");
        }

        private void ResetStreamingAttempt(Action? onRetryCleanup)
        {
            LiveRequestTrace.Current?.MarkRetry();
            if (onRetryCleanup == null)
                return;

            try
            {
                onRetryCleanup();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"⚠️ Streaming retry cleanup failed: {ex.Message}");
            }
        }


        /// <summary>
        /// Send message with automatic retry and rotation (NON-STREAMING version)
        /// NOTE: Does NOT reset model on each call - only on new conversation!
        /// </summary>
        private async Task<string> SendWithRetryAsync(List<ConversationMessage> context, string? imageBase64 = null)
        {
            const int maxAttempts = 5;
            int currentAttempt = 0;
            bool hasTriedModelSwitch = false;
            bool hasTriedKeySwitch = false;
            
            var currentModel = _rotationManager?.GetCurrentModel(_currentProvider) ?? "";
            var totalKeys = _rotationManager?.GetTotalKeyCount(_currentProvider) ?? 0;

            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("SEND WITH RETRY (NON-STREAMING)");
            Log.WriteLine($"Provider: {_currentProvider}");
            Log.WriteLine($"Current model: {currentModel}");
            Log.WriteLine($"Total Keys: {totalKeys}");
            Log.WriteLine($"Auto-switch Keys: {_rotationManager?.IsAutoSwitchKeysEnabled}");
            Log.WriteLine($"Auto-switch Models: {_rotationManager?.IsAutoSwitchModelsEnabled}");
            Log.WriteLine("NOTE: Model persists within this conversation");
            Log.WriteLine("═══════════════════════════════════════════════════════");

            while (currentAttempt < maxAttempts)
            {
                currentAttempt++;
                
                Log.WriteLine($"─────────────────────────────────────────────────────");
                Log.WriteLine($"Attempt #{currentAttempt}/{maxAttempts}");
                
                var currentKeyIndex = _rotationManager?.GetCurrentKeyIndex(_currentProvider) ?? 0;
                currentModel = _rotationManager?.GetCurrentModel(_currentProvider) ?? "";
                
                    Log.WriteLine($"Using: {FormatKeyUsageForLog(currentKeyIndex, totalKeys)}, Model: {currentModel}");
                
                try
                {
                    var response = await _aiService.SendMessageAsync(context, imageBase64);
                    
                    // Check for error responses
                    if (response.StartsWith("Error:"))
                    {
                        Log.WriteLine($"API returned error: {response}");
                        
                        var lowerResponse = response.ToLower();
                        var is429Error = lowerResponse.Contains("429") || lowerResponse.Contains("rate limit");
                        var isRetryable = lowerResponse.Contains("timeout") || 
                                        lowerResponse.Contains("timed out") ||
                                        lowerResponse.Contains("500") ||
                                        lowerResponse.Contains("502") ||
                                        lowerResponse.Contains("503") ||
                                        lowerResponse.Contains("504") ||
                                        lowerResponse.Contains("connection") ||
                                        lowerResponse.Contains("network");
                        var isAuthError = lowerResponse.Contains("401") || 
                                        lowerResponse.Contains("403") ||
                                        lowerResponse.Contains("invalid") && lowerResponse.Contains("key");

                        // ═══════════════════════════════════════════════════════════════
                        // HANDLE 429 RATE LIMIT ERRORS
                        // ═══════════════════════════════════════════════════════════════
                        if (is429Error)
                        {
                            Log.WriteLine("🚫 429 Error Detected (rate limit)");
                            Log.WriteLine("   Strategy: Try all keys → Try models (persist for conversation)");
                            
                            var currentKeyIdx = _rotationManager?.GetCurrentKeyIndex(_currentProvider) ?? 0;
                            _rotationManager?.MarkKeyAsRateLimited(_currentProvider, currentKeyIdx);
                            var availableKeys = _rotationManager?.GetAvailableKeyCount(_currentProvider) ?? 0;
                            var totalKeysCount = _rotationManager?.GetTotalKeyCount(_currentProvider) ?? 0;
                            
                            // Try next key if available
                            if (_rotationManager != null && 
                                _rotationManager.IsAutoSwitchKeysEnabled &&
                                availableKeys > 0)
                            {
                                var oldKeyIndex = currentKeyIdx;
                                var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                
                                _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                
                                var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                
                                Log.WriteLine($"   ✓ Switched from Key #{oldKeyIndex + 1} to Key #{newKeyIndex + 1}");
                                
                                APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1}/{totalKeysCount} (rate limit)");
                                
                                await Task.Delay(1000);
                                continue;
                            }
                            // All keys exhausted - try different model (persists for this conversation)
                            else if (!hasTriedModelSwitch && 
                                    _rotationManager != null &&
                                    _rotationManager.IsAutoSwitchModelsEnabled &&
                                    _rotationManager.HasMultipleModels(_currentProvider))
                            {
                                Log.WriteLine("   All keys exhausted - trying different model");
                                Log.WriteLine("   → Model will persist for rest of this conversation");
                                
                                var oldModel = _rotationManager.GetCurrentModel(_currentProvider);
                                var newModel = _rotationManager.GetNextModel(_currentProvider);
                                
                                // Reset to first key with new model
                                _rotationManager.ClearRateLimitedKeys(_currentProvider);
                                _rotationManager.ResetKeyRotation(_currentProvider);
                                var firstKey = _rotationManager.GetNextApiKey(_currentProvider);
                                
                                _aiService = AIServiceFactory.CreateService(_currentProvider, firstKey, newModel);
                                hasTriedModelSwitch = true;
                                
                                Log.WriteLine($"   ✓ Switched to model: {newModel} with Key #1");
                                
                                APISwitchNotification?.Invoke(this, $"🔄 Switched model: {oldModel} → {newModel} (all keys rate limited)");
                                
                                await Task.Delay(1000);
                                continue;
                            }
                            else
                            {
                                // All keys AND models exhausted
                                Log.WriteLine("   ✗ All API keys and models exhausted");
                                
                                var errorMessage = $"⚠️ **All API keys rate limited**\n\n" +
                                                $"All {totalKeysCount} API key(s) have hit rate limits.\n\n" +
                                                (hasTriedModelSwitch ? "Also tried alternative models.\n\n" : "") +
                                                $"Please wait a few minutes or add more API keys in Settings.";
                                
                                return errorMessage;
                            }
                        }
                        // ═══════════════════════════════════════════════════════════════
                        // HANDLE TIMEOUT AND SERVER ERRORS
                        // ═══════════════════════════════════════════════════════════════
                        else if (isRetryable)
                        {
                            Log.WriteLine("🔄 Retryable Error Detected (timeout/server error)");
                            Log.WriteLine("   Strategy: Retry once → Switch model → Switch key");
                            
                            if (currentAttempt == 1)
                            {
                                // First retry - same key and model
                                Log.WriteLine("   Retry 1: Same key/model after 2s delay");
                                await Task.Delay(2000);
                                continue;
                            }
                            else if (!hasTriedModelSwitch && 
                                    _rotationManager != null &&
                                    _rotationManager.IsAutoSwitchModelsEnabled &&
                                    _rotationManager.HasMultipleModels(_currentProvider))
                            {
                                // Second retry - switch model (persists for conversation)
                                Log.WriteLine("   Retry 2: Switching model");
                                Log.WriteLine("   → Model will persist for rest of this conversation");
                                
                                var oldModel = _rotationManager.GetCurrentModel(_currentProvider);
                                var newModel = _rotationManager.GetNextModel(_currentProvider);
                                var currentKey = _rotationManager.GetNextApiKey(_currentProvider);
                                
                                _aiService = AIServiceFactory.CreateService(_currentProvider, currentKey, newModel);
                                hasTriedModelSwitch = true;
                                
                                Log.WriteLine($"   ✓ Switched to model: {newModel}");
                                
                                APISwitchNotification?.Invoke(this, $"🔄 Switched model: {oldModel} → {newModel} (timeout)");
                                
                                await Task.Delay(1000);
                                continue;
                            }
                            else if (!hasTriedKeySwitch &&
                                    _rotationManager != null &&
                                    _rotationManager.IsAutoSwitchKeysEnabled &&
                                    _rotationManager.GetTotalKeyCount(_currentProvider) > 1)
                            {
                                // Third retry - switch key
                                Log.WriteLine("   Retry 3: Switching key");
                                
                                var oldKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                
                                _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                hasTriedKeySwitch = true;
                                
                                var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                var totalKeysCount = _rotationManager.GetTotalKeyCount(_currentProvider);
                                
                                Log.WriteLine($"   ✓ Switched from Key #{oldKeyIndex + 1} to Key #{newKeyIndex + 1}");
                                
                                APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1}/{totalKeysCount}");
                                
                                await Task.Delay(1000);
                                continue;
                            }
                            else
                            {
                                Log.WriteLine("   ✗ All retry strategies exhausted");
                                
                                var errorMessage = $"⚠️ **Request failed after retries**\n\n" +
                                                $"Error: {response}\n\n" +
                                                $"Tried: {currentAttempt} attempts" +
                                                (hasTriedModelSwitch ? ", model switch" : "") +
                                                (hasTriedKeySwitch ? ", key switch" : "");
                                
                                return errorMessage;
                            }
                        }
                        // ═══════════════════════════════════════════════════════════════
                        // HANDLE AUTH ERRORS (mark key as failed)
                        // ═══════════════════════════════════════════════════════════════
                        else if (isAuthError)
                        {
                            Log.WriteLine("🚫 Authentication Error - Key is invalid");
                            
                            if (_rotationManager != null)
                            {
                                _rotationManager.MarkKeyAsFailed(_currentProvider, currentKeyIndex);
                                Log.WriteLine($"   Marked Key #{currentKeyIndex + 1} as failed");
                                
                                if (_rotationManager.GetRecoverableKeyCount(_currentProvider) > 0)
                                {
                                    var newKey = _rotationManager.GetNextApiKey(_currentProvider);
                                    var currentModelName = _rotationManager.GetCurrentModel(_currentProvider);
                                    _aiService = AIServiceFactory.CreateService(_currentProvider, newKey, currentModelName);
                                    
                                    var newKeyIndex = _rotationManager.GetCurrentKeyIndex(_currentProvider);
                                    Log.WriteLine($"   Switched to Key #{newKeyIndex + 1}");
                                    
                                    APISwitchNotification?.Invoke(this, $"🔄 Switched to Key #{newKeyIndex + 1} (invalid key)");
                                    
                                    await Task.Delay(500);
                                    continue;
                                }
                            }
                            
                            return $"⚠️ **Invalid API Key**\n\n{response}\n\nPlease check your API key in Settings.";
                        }
                        else
                        {
                            // Unknown error - don't retry
                            Log.WriteLine($"   Non-retryable error: {response}");
                            return response;
                        }
                    }
                    else
                    {
                        // Success!
                        Log.WriteLine($"✓ Request successful (attempt #{currentAttempt})");
                        Log.WriteLine($"✓ Conversation will continue with model: {currentModel}");
                        Log.WriteLine("═══════════════════════════════════════════════════════");
                        return response;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"✗ Exception on attempt #{currentAttempt}: {ex.Message}");
                    
                    if (currentAttempt >= maxAttempts)
                    {
                        return $"Error: {ex.Message}";
                    }
                    
                    await Task.Delay(1000);
                }
            }

            Log.WriteLine("✗ Max attempts reached - giving up");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            return $"Error: Maximum retry attempts ({maxAttempts}) exceeded";
        }




        private bool IsRetryableError(string error)
        {
            var retryableErrors = new[]
            {
                "TooManyRequests",
                "rate_limit",
                "capacity",
                "overloaded",
                "timeout",
                "503",
                "502",
                "500"
            };

            return retryableErrors.Any(e => error.Contains(e, StringComparison.OrdinalIgnoreCase));
        }

        // ═══════════════════════════════════════════════════════════════
        // UTILITY METHODS
        // ═══════════════════════════════════════════════════════════════

        public List<ConversationMessage> GetAllMessages()
        {
            // Return all messages except system prompt (for UI display)
            return _fullConversation.Skip(1).ToList();
        }

        public string? RemoveLastExchangeForRegeneration()
        {
            if (_fullConversation.Count < 3
                || !string.Equals(_fullConversation[^1].Role, "assistant", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_fullConversation[^2].Role, "user", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var question = _fullConversation[^2].Content;
            _fullConversation.RemoveRange(_fullConversation.Count - 2, 2);
            Log.WriteLine("Removed the last user/assistant exchange for regeneration.");
            return question;
        }

        /// <summary>
        /// Clear entire conversation and reset to preferred model
        /// </summary>
        public void ClearConversation()
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("CLEARING CONVERSATION");
            Log.WriteLine($"  Messages before clear: {_fullConversation.Count}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            // Clear everything
            _fullConversation.Clear();
            _resumeSummarized = false;
            _jobDescriptionSummarized = false;
            _structuredKnowledgeContext = string.Empty;
            _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
            _lastRetrievedDocumentIds = Array.Empty<string>();
            _activeProjectCardId = string.Empty;
            
            UpdateSystemPromptWithContext();
            
            Log.WriteLine($"  ✓ System prompt re-added");
            Log.WriteLine($"  Messages after clear: {_fullConversation.Count}");
            
            // Reset model rotation to preferred model for new conversation
            _rotationManager?.StartNewConversation(_currentProvider);
            
            Log.WriteLine("✓ Conversation cleared");
            Log.WriteLine("✓ Reset to preferred model");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }


        /// <summary>
        /// Start new topic (keeps resume/JD summaries but clears conversation)
        /// </summary>
        public void StartNewTopic()
        {
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("STARTING NEW TOPIC");
            Log.WriteLine($"  Messages before clear: {_fullConversation.Count}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            // Clear conversation history (but keep summarization flags)
            _fullConversation.Clear();
            _structuredKnowledgeContext = string.Empty;
            _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
            _lastRetrievedDocumentIds = Array.Empty<string>();
            _activeProjectCardId = string.Empty;
            
            // ✅ CRITICAL FIX: Re-add system prompt with resume context
            UpdateSystemPromptWithContext();
            
            Log.WriteLine($"  Messages after clear: {_fullConversation.Count}");
            
            // Reset model rotation to preferred model for new topic
            _rotationManager?.StartNewConversation(_currentProvider);
            
            Log.WriteLine("✓ New topic started");
            Log.WriteLine("✓ Kept: Resume summary, JD summary");
            Log.WriteLine("✓ Cleared: Conversation history");
            Log.WriteLine("✓ Reset to preferred model");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }


        /// <summary>
        /// Change AI provider (resets everything)
        /// </summary>
        public void ChangeProvider(string newProvider)
        {
            _currentProvider = newProvider;
            
            // Reset conversation model state for all providers
            _rotationManager?.ResetAllConversationState();
            
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine($"✓ Changed provider to: {newProvider}");
            Log.WriteLine("✓ Reset all conversation model preferences");
            Log.WriteLine("═══════════════════════════════════════════════════════");
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Rough estimation: 1 token ≈ 4 characters
            // More accurate for English text
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        public int GetTotalTokens()
        {
            return _fullConversation.Sum(m => m.EstimatedTokens);
        }

        public int GetContextTokens()
        {
            var context = BuildOptimizedContext();
            return context.Sum(m => m.EstimatedTokens);
        }

        public bool CanSendMessage()
        {
            var contextTokens = GetContextTokens();
            var available = _modelConfig.MaxContextTokens - contextTokens - _modelConfig.MaxResponseTokens;
            return available > 100;
        }

        private string TruncateMessage(string text, int maxChars)
        {
            if (text.Length <= maxChars)
                return text;

            return text.Substring(0, maxChars) + "...";
        }

        public List<ConversationMessage> GetOptimizedContextForDebug()
        {
            return BuildOptimizedContext();
        }

        // ═══════════════════════════════════════════════════════════════
        // CONVERSATION EXPORT/IMPORT (for restart feature)
        // ═══════════════════════════════════════════════════════════════

        public List<ConversationMessage> ExportConversation()
        {
            // Return all messages (including system prompt)
            return new List<ConversationMessage>(_fullConversation);
        }

        public void ImportConversation(List<ConversationMessage> messages)
        {
            if (messages == null || messages.Count == 0)
            {
                Log.WriteLine("No conversation to import");
                return;
            }

            Log.WriteLine($"Importing {messages.Count} messages...");
            Log.WriteLine("─────────────────────────────────────────────────────");

            // IMPORTANT: Clear current conversation first
            _fullConversation.Clear();
            _structuredKnowledgeContext = string.Empty;
            _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
            _lastRetrievedDocumentIds = Array.Empty<string>();
            _activeProjectCardId = string.Empty;

            // Import all messages (including system prompt with resume/JD)
            foreach (var msg in messages)
            {
                _fullConversation.Add(new ConversationMessage
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    Summary = msg.Summary,
                    Timestamp = msg.Timestamp,
                    HasCode = msg.HasCode,
                    EstimatedTokens = msg.EstimatedTokens
                });
                
                Log.WriteLine($"  [{msg.Role}] {msg.EstimatedTokens} tokens");
            }

            Log.WriteLine("─────────────────────────────────────────────────────");
            Log.WriteLine($"✓ Imported {_fullConversation.Count} messages");
            Log.WriteLine($"  - System: {_fullConversation.Count(m => m.Role == "system")}");
            Log.WriteLine($"  - User: {_fullConversation.Count(m => m.Role == "user")}");
            Log.WriteLine($"  - Assistant: {_fullConversation.Count(m => m.Role == "assistant")}");
            Log.WriteLine($"  - Total tokens: ~{_fullConversation.Sum(m => m.EstimatedTokens)}");
            
            // Extract resume summary if present in system prompt
            var systemMsg = _fullConversation.FirstOrDefault(m => m.Role == "system");
            if (systemMsg != null)
            {
                Log.WriteLine($"  System prompt length: {systemMsg.Content.Length} chars");

                RestoreEmbeddedContextFromSystemPrompt(systemMsg.Content);
            }
        }

        private void RestoreEmbeddedContextFromSystemPrompt(string systemPromptContent)
        {
            const string resumeMarker = "\n\nUser Profile: ";
            const string jdMarker = "\n\nInterview Context: You are helping the user prepare for an interview for the following position. Provide relevant advice, practice questions, and feedback based on this job description:\n";

            int resumeStart = systemPromptContent.IndexOf(resumeMarker, StringComparison.Ordinal);
            int jdStart = systemPromptContent.IndexOf(jdMarker, StringComparison.Ordinal);

            if (resumeStart >= 0)
            {
                int contentStart = resumeStart + resumeMarker.Length;
                int contentEnd = jdStart >= 0 && jdStart > contentStart ? jdStart : systemPromptContent.Length;
                var extractedResumeSummary = systemPromptContent.Substring(contentStart, contentEnd - contentStart).Trim();

                if (!string.IsNullOrWhiteSpace(extractedResumeSummary))
                {
                    _resumeSummary = extractedResumeSummary;
                    _resumeSummarized = true;
                    Log.WriteLine("  ✓ Resume summary restored from imported system prompt");
                }
            }

            if (jdStart >= 0)
            {
                int contentStart = jdStart + jdMarker.Length;
                var extractedJdSummary = systemPromptContent.Substring(contentStart).Trim();

                if (!string.IsNullOrWhiteSpace(extractedJdSummary))
                {
                    _jobDescriptionSummary = extractedJdSummary;
                    _jobDescriptionSummarized = true;
                    Log.WriteLine("  ✓ Job description summary restored from imported system prompt");
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // VALIDATE AND CLEAN MESSAGES (for strict APIs like Mistral)
        // ═══════════════════════════════════════════════════════════════
        private List<ConversationMessage> ValidateAndCleanMessages(List<ConversationMessage> messages)
        {
            var cleaned = messages
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .ToList();
            
            if (cleaned.Count < messages.Count)
            {
                Log.WriteLine($"⚠️ Filtered out {messages.Count - cleaned.Count} empty messages");
            }
            
            // Ensure we have at least system + user message
            if (cleaned.Count < 2)
            {
                Log.WriteLine($"⚠️ Warning: Only {cleaned.Count} messages after filtering");
            }
            
            // Log message structure for debugging
            Log.WriteLine($"Message structure:");
            foreach (var msg in cleaned)
            {
                var preview = msg.Content.Length > 50 ? msg.Content.Substring(0, 50) + "..." : msg.Content;
                Log.WriteLine($"  [{msg.Role}] {msg.Content.Length} chars: {preview}");
            }
            
            return cleaned;
        }

        public int GetMessageCount()
        {
            return _fullConversation.Count;
        }

    }
}
