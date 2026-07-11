using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Infrastructure.Hosted.Contracts;

namespace SecureOverlay.Services
{
    public class ConversationManager
    {
        private const int RecentFullMessageCount = 10;
        private const int PlannerRecentMessageCount = 10;

        private List<ConversationMessage> _fullConversation = new List<ConversationMessage>();
        private string _systemPrompt = "";
        private string _resumeText = string.Empty;
        private string _resumeSummary = string.Empty;
        private bool _resumeSummarized = false;

        // NEW: Job Description fields
        private string _jobDescriptionText = string.Empty;
        private string _jobDescriptionSummary = string.Empty;
        private bool _jobDescriptionSummarized = false;
        private string _structuredKnowledgeContext = string.Empty;
        private string _activeProjectCardId = string.Empty;
        private IReadOnlyList<RetrievedContextSnippet> _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
        private IReadOnlyList<string> _lastRetrievedDocumentIds = Array.Empty<string>();
        private readonly Func<string, IReadOnlyList<string>?, CancellationToken, Task<IReadOnlyList<RetrievedContextSnippet>>>? _knowledgeRetriever;
        private readonly Func<CancellationToken, Task<HostedKnowledgeBaseSummaryDto>>? _knowledgeBaseLoader;
        private HostedKnowledgeBaseSummaryDto? _knowledgeBaseSummaryCache;
        private Task<HostedKnowledgeBaseSummaryDto?>? _knowledgeBaseSummaryLoadTask;
        private Task? _interviewContextPackWarmupTask;
        private DateTime _knowledgeBaseSummaryCachedAtUtc = DateTime.MinValue;
        private static readonly TimeSpan KnowledgeBaseSummaryCacheTtl = TimeSpan.FromSeconds(30);
        private readonly Dictionary<string, CachedInterviewContextPack> _interviewContextPacks = new(StringComparer.Ordinal);
        private static readonly TimeSpan InterviewContextPackTtl = TimeSpan.FromMinutes(5);
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
            Func<CancellationToken, Task<HostedKnowledgeBaseSummaryDto>>? knowledgeBaseLoader = null)
        {
            _aiService = aiService;
            _systemPrompt = systemPrompt;
            _modelConfig = modelConfig;
            _rotationManager = rotationManager;
            _currentProvider = aiService.GetProviderName();
            _knowledgeRetriever = knowledgeRetriever;
            _knowledgeBaseLoader = knowledgeBaseLoader;

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

        private async Task<bool> SummarizeResumeAsync()
        {
            if (_resumeSummarized || string.IsNullOrWhiteSpace(_resumeText))
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
                        Content = _resumeText
                    }
                };

                var summary = await _aiService.SendMessageAsync(summarizationPrompt);

                if (!summary.StartsWith("Error:"))
                {
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
            
            UpdateSystemPromptWithContext();
            
            Log.WriteLine("✓ Job description cleared");
        }

        public string GetJobDescriptionSummary() => _jobDescriptionSummary;

        public bool HasJobDescription() => !string.IsNullOrWhiteSpace(_jobDescriptionText);

        private async Task<bool> SummarizeJobDescriptionAsync()
        {
            if (_jobDescriptionSummarized || string.IsNullOrWhiteSpace(_jobDescriptionText))
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
                        Content = _jobDescriptionText
                    }
                };

                var summary = await _aiService.SendMessageAsync(summarizationPrompt);

                if (!summary.StartsWith("Error:"))
                {
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
                    _jobDescriptionSummary
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

            if (!string.IsNullOrWhiteSpace(_jobDescriptionSummary))
            {
                contextParts.Append("\n\nInterview Context: You are helping the user prepare for an interview for the following position. Provide relevant advice, practice questions, and feedback based on this job description:\n");
                contextParts.Append(_jobDescriptionSummary);
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

            // Summarize resume on first user message
            if (HasResume() && !_resumeSummarized)
            {
                var summarized = await SummarizeResumeAsync();
                if (!summarized)
                {
                    _fullConversation.RemoveAt(_fullConversation.Count - 1);
                    return ("", "Failed to summarize resume. Please try again.");
                }
            }
            
            // Summarize JD on first user message
            if (HasJobDescription() && !_jobDescriptionSummarized)
            {
                Log.WriteLine("✓ JD needs summarization - calling SummarizeJobDescriptionAsync()");
                var summarized = await SummarizeJobDescriptionAsync();
                if (!summarized)
                {
                    _fullConversation.RemoveAt(_fullConversation.Count - 1);
                    return ("", "Failed to summarize job description. Please try again.");
                }
            }

            PrimeKnowledgeBaseSummaryLoad(CancellationToken.None);

            // ponytail: cheap local routing handles obvious interview asks; model planner stays for ambiguous turns.
            var plannerDecision = await PlanResponseAsync(userMessage, imageBase64, CancellationToken.None);
            await ApplyPlannerDecisionAsync(plannerDecision, userMessage, CancellationToken.None);

            var retrievalMissResponse = BuildRetrievalMissResponse(plannerDecision);
            if (!string.IsNullOrWhiteSpace(retrievalMissResponse))
            {
                RagTraceLogger.WriteLine("retrieval:miss short_circuit_response=true");
                return CompleteAssistantResponse(retrievalMissResponse);
            }

            if (plannerDecision.Type == ResponsePlanType.Direct
                && !string.IsNullOrWhiteSpace(plannerDecision.DirectAnswer))
            {
                RagTraceLogger.WriteLine("planner:direct_answer_used=true");
                return CompleteAssistantResponse(plannerDecision.DirectAnswer);
            }

            // Build optimized context for API
            var optimizedContext = BuildOptimizedContext();

            // Log context info
            var totalTokens = optimizedContext.Sum(m => m.EstimatedTokens);
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

            // Summarize resume on first user message
            if (HasResume() && !_resumeSummarized)
            {
                var summarized = await SummarizeResumeAsync();
                if (!summarized)
                {
                    _fullConversation.RemoveAt(_fullConversation.Count - 1);
                    return ("", "Failed to summarize resume. Please try again.");
                }
            }

            // Summarize JD on first user message
            if (HasJobDescription() && !_jobDescriptionSummarized)
            {
                var summarized = await SummarizeJobDescriptionAsync();
                if (!summarized)
                {
                    _fullConversation.RemoveAt(_fullConversation.Count - 1);
                    return ("", "Failed to summarize job description. Please try again.");
                }
            }

            PrimeKnowledgeBaseSummaryLoad(cancellationToken);

            // ponytail: one planner call decides direct vs retrieve; no second router or word gate.
            var plannerDecision = await PlanResponseAsync(userMessage, imageBase64, cancellationToken);
            await ApplyPlannerDecisionAsync(plannerDecision, userMessage, cancellationToken);

            var retrievalMissResponse = BuildRetrievalMissResponse(plannerDecision);
            if (!string.IsNullOrWhiteSpace(retrievalMissResponse))
            {
                RagTraceLogger.WriteLine("retrieval:miss short_circuit_response=true [streaming]");
                onChunkReceived?.Invoke(retrievalMissResponse);
                return CompleteAssistantResponse(retrievalMissResponse);
            }

            if (plannerDecision.Type == ResponsePlanType.Direct
                && !string.IsNullOrWhiteSpace(plannerDecision.DirectAnswer))
            {
                RagTraceLogger.WriteLine("planner:direct_answer_used=true [streaming]");
                onChunkReceived?.Invoke(plannerDecision.DirectAnswer);
                return CompleteAssistantResponse(plannerDecision.DirectAnswer);
            }

            // Build optimized context for API
            var optimizedContext = BuildOptimizedContext();

            var totalTokens = optimizedContext.Sum(m => m.EstimatedTokens);
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

        private async Task<ResponsePlan> PlanResponseAsync(
            string userMessage,
            string? imageBase64,
            CancellationToken cancellationToken)
        {
            if (_knowledgeBaseLoader != null && !HasFreshKnowledgeBaseSummaryCache())
            {
                await LoadKnowledgeBaseSummaryAsync(cancellationToken);
            }

            RagTraceLogger.WriteLine(
                $"planner:start question='{TrimForLog(userMessage, 240)}' image_attached={imageBase64 != null} prior_turns={Math.Max(0, _fullConversation.Count - 2)} previous_snippets={_retrievedKnowledgeSnippets.Count}");
            if (TryBuildHeuristicResponsePlan(userMessage, out var heuristicPlan))
            {
                RagTraceLogger.WriteLine(
                    $"planner:heuristic type={heuristicPlan.Type} scope={heuristicPlan.Scope} target='{TrimForLog(heuristicPlan.Target, 120)}' knowledge_query='{TrimForLog(heuristicPlan.KnowledgeQuery, 240)}'");
                return heuristicPlan;
            }

            if (!_aiService.IsConfigured())
            {
                RagTraceLogger.WriteLine("planner:ai_not_configured fallback=retrieve");
                return ResponsePlan.Retrieve(userMessage, "ai-not-configured");
            }

            var plannerMessages = BuildPlannerMessages(userMessage);
            var plannerResponse = await SendPlannerRequestAsync(plannerMessages, imageBase64, cancellationToken);
            RagTraceLogger.WriteLine($"planner:raw_response {TrimForLog(plannerResponse, 1200)}");
            if (plannerResponse.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                Log.WriteLine($"⚠️ Planner failed, falling back to retrieval: {plannerResponse}");
                RagTraceLogger.WriteLine("planner:error fallback=retrieve");
                return ResponsePlan.Retrieve(userMessage, "planner-error");
            }

            if (TryParseResponsePlan(plannerResponse, userMessage, out var plan))
            {
                Log.WriteLine(
                    $"Planner decision: Type={plan.Type}, Confidence={plan.Confidence:0.00}, KnowledgeQuery='{plan.KnowledgeQuery}'");
                RagTraceLogger.WriteLine(
                    $"planner:decision type={plan.Type} scope={plan.Scope} confidence={plan.Confidence:0.00} target='{TrimForLog(plan.Target, 120)}' direct_answer='{TrimForLog(plan.DirectAnswer, 240)}' knowledge_query='{TrimForLog(plan.KnowledgeQuery, 240)}'");
                return plan;
            }

            Log.WriteLine("⚠️ Planner returned invalid JSON. Falling back to retrieval.");
            RagTraceLogger.WriteLine("planner:invalid_json fallback=retrieve");
            return ResponsePlan.Retrieve(userMessage, "planner-parse-fallback");
        }

        private async Task<string> SendPlannerRequestAsync(
            List<ConversationMessage> messages,
            string? imageBase64,
            CancellationToken cancellationToken)
        {
            var builder = new System.Text.StringBuilder();
            return await _aiService.SendMessageStreamAsync(
                messages,
                chunk => builder.Append(chunk),
                cancellationToken,
                imageBase64);
        }

        private List<ConversationMessage> BuildPlannerMessages(string userMessage)
        {
            var priorTurns = _fullConversation
                .Skip(1)
                .Take(Math.Max(0, _fullConversation.Count - 2))
                .TakeLast(PlannerRecentMessageCount)
                .Select(message => $"{message.Role}: {TruncateMessage(message.Content, 280)}")
                .ToArray();
            var previousRetrieval = _retrievedKnowledgeSnippets
                .Take(2)
                .Select(snippet => $"{snippet.DocumentTitle}: {TruncateMessage(snippet.Text, 180)}")
                .ToArray();

            var plannerContext = new List<string>
            {
                $"Current question: {userMessage}",
                $"Recent conversation:\n{(priorTurns.Length == 0 ? "(none)" : string.Join("\n", priorTurns))}",
                $"Previous retrieval context:\n{(previousRetrieval.Length == 0 ? "(none)" : string.Join("\n", previousRetrieval))}",
                $"Structured KB snapshot:\n{BuildPlannerKnowledgeBaseHint()}"
            };

            return new List<ConversationMessage>
            {
                new ConversationMessage
                {
                    Role = "system",
                    Content = @"You are a routing planner for an interview assistant.
Return strict JSON only with this shape:
{""type"":""direct|profile|project|retrieve"",""direct_answer"":""..."",""knowledge_query"":""..."",""scope"":""global|previous_docs|active_project"",""target"":""..."",""confidence"":0.0}

Rules:
- type=direct when the user can be answered from general knowledge or recent generic conversation.
- type=profile when the answer should come from the user's background, intro, resume, strengths, skills, current role, or experience summary.
- type=project when the answer should come from one of the user's projects, including project overview, architecture, tech stack, role, challenges, or impact.
- type=retrieve when the answer should be grounded in uploaded knowledge-base content that is not primarily the structured profile/project material.
- In interview context, prompts like ""introduce yourself"", ""tell me about yourself"", ""walk me through your background"", and ""summarize your experience"" are about the USER, so use type=profile.
- In interview context, prompts like ""tell me about any of your projects"", ""tell me about a recent project"", ""what did you build"", ""explain the architecture of this"", and ""what challenges did you face"" after a project discussion should use type=project.
- Never answer resume, project, or background questions as the assistant's own identity or invented experience.
- For broad self-introduction prompts, rewrite knowledge_query to something semantically rich like ""candidate background summary experience skills"" instead of copying the raw wording.
- For broad project prompts, rewrite knowledge_query to something semantically rich like ""recent project architecture technologies impact role"" instead of copying the raw wording.
- For follow-ups like ""this"", ""that"", ""it"", or ""that project"", rewrite knowledge_query to the specific project/topic from recent conversation.
- Use scope=""active_project"" only when the request clearly continues the same project already in discussion.
- Use scope=""previous_docs"" only when the request clearly continues the same retrieved document/topic outside the project route.
- Use scope=""global"" when the user is switching topics, naming a different project, or asking a fresh question.
- When a specific project name is clear, put that project name into target.
- For generic follow-ups like ""give me an example"" after a conceptual question, use type=direct.
- If type=direct, include a complete direct_answer and leave knowledge_query empty.
- If type=profile, project, or retrieve, leave direct_answer empty and include a short, specific knowledge_query.
- If unsure between direct and a grounded route, prefer the grounded route.
- Do not include markdown fences or extra text."
                },
                new ConversationMessage
                {
                    Role = "user",
                    Content = string.Join("\n\n", plannerContext)
                }
                };
        }

        private bool TryBuildHeuristicResponsePlan(string userMessage, out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "heuristic-none");
            var normalized = NormalizeText(userMessage);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            if (TryBuildHeuristicProjectPlan(userMessage, normalized, out plan))
            {
                return true;
            }

            if (TryBuildHeuristicProfilePlan(userMessage, normalized, out plan))
            {
                return true;
            }

            return false;
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

        private bool TryBuildHeuristicProjectPlan(
            string userMessage,
            string normalizedUserMessage,
            out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "heuristic-none");
            var namedProjectTarget = ExtractLikelyProjectTarget(normalizedUserMessage);
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
            return true;
        }

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

        private static bool TryParseResponsePlan(string plannerResponse, string userMessage, out ResponsePlan plan)
        {
            plan = ResponsePlan.Retrieve(userMessage, "default");
            var json = ExtractJsonObject(plannerResponse);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var type = root.TryGetProperty("type", out var typeElement)
                    ? (typeElement.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                    : string.Empty;
                var directAnswer = root.TryGetProperty("direct_answer", out var answerElement)
                    ? (answerElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var knowledgeQuery = root.TryGetProperty("knowledge_query", out var queryElement)
                    ? (queryElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(knowledgeQuery)
                    && root.TryGetProperty("rag_query", out var ragQueryElement))
                {
                    knowledgeQuery = (ragQueryElement.GetString() ?? string.Empty).Trim();
                }
                var scope = root.TryGetProperty("scope", out var scopeElement)
                    ? (scopeElement.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                    : string.Empty;
                var target = root.TryGetProperty("target", out var targetElement)
                    ? (targetElement.GetString() ?? string.Empty).Trim()
                    : string.Empty;
                var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                    && confidenceElement.ValueKind == JsonValueKind.Number
                    && confidenceElement.TryGetDouble(out var parsedConfidence)
                    ? parsedConfidence
                    : 0d;
                var retrievalScope = scope switch
                {
                    "previous_docs" => RetrievalScope.PreviousDocuments,
                    "active_project" => RetrievalScope.ActiveProject,
                    _ => RetrievalScope.Global
                };

                if (type == "direct")
                {
                    plan = ResponsePlan.Direct(directAnswer, confidence);
                    return true;
                }

                if (type == "profile")
                {
                    plan = ResponsePlan.Profile(
                        string.IsNullOrWhiteSpace(knowledgeQuery) ? userMessage : knowledgeQuery,
                        target,
                        retrievalScope,
                        confidence);
                    return true;
                }

                if (type == "project")
                {
                    plan = ResponsePlan.Project(
                        string.IsNullOrWhiteSpace(knowledgeQuery) ? userMessage : knowledgeQuery,
                        target,
                        retrievalScope,
                        confidence);
                    return true;
                }

                if (type == "retrieve")
                {
                    plan = ResponsePlan.Retrieve(
                        string.IsNullOrWhiteSpace(knowledgeQuery) ? userMessage : knowledgeQuery,
                        "planner",
                        retrievalScope,
                        target,
                        confidence);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task ApplyPlannerDecisionAsync(
            ResponsePlan plannerDecision,
            string userMessage,
            CancellationToken cancellationToken)
        {
            switch (plannerDecision.Type)
            {
                case ResponsePlanType.Direct:
                    ClearStructuredKnowledgeContext();
                    ClearRetrievedKnowledgeSnippets();
                    return;

                case ResponsePlanType.Profile:
                    ClearRetrievedKnowledgeSnippets();
                    await PrepareProfileGroundingAsync(plannerDecision, userMessage, cancellationToken);
                    return;

                case ResponsePlanType.Project:
                    await PrepareProjectGroundingAsync(plannerDecision, userMessage, cancellationToken);
                    return;

                case ResponsePlanType.Retrieve:
                default:
                    ClearStructuredKnowledgeContext();
                    await RefreshRetrievedKnowledgeSnippetsAsync(
                        plannerDecision.KnowledgeQuery,
                        plannerDecision.Scope == RetrievalScope.PreviousDocuments ? _lastRetrievedDocumentIds : null,
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
            if (!HasProfileGrounding(profileCard))
            {
                ClearStructuredKnowledgeContext();
                RagTraceLogger.WriteLine("profile_grounding:missing");
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
                ClearStructuredKnowledgeContext();
                ClearRetrievedKnowledgeSnippets();
                RagTraceLogger.WriteLine("project_grounding:missing");
                return;
            }

            var groundedProject = selectedProject!;
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
            return _knowledgeBaseSummaryCache != null
                && DateTime.UtcNow - _knowledgeBaseSummaryCachedAtUtc <= KnowledgeBaseSummaryCacheTtl;
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
                _knowledgeBaseSummaryCache = await _knowledgeBaseLoader!(cancellationToken);
                _knowledgeBaseSummaryCachedAtUtc = DateTime.UtcNow;
                _interviewContextPacks.Clear();
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
                    await CacheProfilePackAsync(knowledgeBase.ProfileCard, InterviewPackKind.ProfileIntro, cancellationToken);
                    await CacheProfilePackAsync(knowledgeBase.ProfileCard, InterviewPackKind.ProfileStrengths, cancellationToken);
                    await CacheProfilePackAsync(knowledgeBase.ProfileCard, InterviewPackKind.ProfileRole, cancellationToken);
                }

                var primaryProject = (knowledgeBase.ProjectCards ?? Array.Empty<HostedKnowledgeBaseProjectCardDto>())
                    .OrderByDescending(card => card.IsRecent)
                    .ThenBy(card => card.SortOrder)
                    .FirstOrDefault();
                if (HasProjectGrounding(primaryProject))
                {
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectOverview, cancellationToken);
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectArchitecture, cancellationToken);
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectChallenges, cancellationToken);
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectStack, cancellationToken);
                    await CacheProjectPackAsync(primaryProject!, InterviewPackKind.ProjectImpact, cancellationToken);
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
                CachedAtUtc = DateTime.UtcNow,
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
                CachedAtUtc = DateTime.UtcNow,
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
            if (_interviewContextPacks.TryGetValue(packKey, out pack!)
                && DateTime.UtcNow - pack.CachedAtUtc <= InterviewContextPackTtl)
            {
                return true;
            }

            _interviewContextPacks.Remove(packKey);
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
                || !string.IsNullOrWhiteSpace(profileCard.ResumeText)
                || !string.IsNullOrWhiteSpace(profileCard.FullName)
                || !string.IsNullOrWhiteSpace(profileCard.CurrentRole)
                || profileCard.Skills.Count > 0
                || profileCard.Strengths.Count > 0
                || profileCard.Domains.Count > 0;
        }

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

        private static string BuildProfilePackCacheKey(InterviewPackKind packKind)
        {
            return $"profile:{packKind}";
        }

        private static string BuildProjectPackCacheKey(string projectCardId, InterviewPackKind packKind)
        {
            return $"project:{projectCardId}:{packKind}";
        }

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
                    AppendGroundingLine(lines, "Current role", profileCard.CurrentRole);
                    if (profileCard.YearsOfExperience > 0)
                    {
                        lines.Add($"Years of experience: {profileCard.YearsOfExperience}");
                    }
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingList(lines, "Skills", profileCard.Skills.Take(6).ToArray());
                    break;

                case InterviewPackKind.ProfileStrengths:
                    AppendGroundingList(lines, "Strengths", profileCard.Strengths);
                    AppendGroundingList(lines, "Skills", profileCard.Skills);
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingLine(lines, "Resume details", TruncateMessageStatic(profileCard.ResumeText, 600));
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
                    AppendGroundingLine(lines, "Current role", profileCard.CurrentRole);
                    if (profileCard.YearsOfExperience > 0)
                    {
                        lines.Add($"Years of experience: {profileCard.YearsOfExperience}");
                    }
                    AppendGroundingList(lines, "Strengths", profileCard.Strengths);
                    AppendGroundingList(lines, "Skills", profileCard.Skills);
                    AppendGroundingList(lines, "Domains", profileCard.Domains);
                    AppendGroundingLine(lines, "Resume details", TruncateMessageStatic(profileCard.ResumeText, 1200));
                    break;
            }

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

        private static string ExtractJsonObject(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var fencedMatch = Regex.Match(
                value,
                "```(?:json)?\\s*(\\{.*\\})\\s*```",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (fencedMatch.Success)
            {
                return fencedMatch.Groups[1].Value.Trim();
            }

            var firstBrace = value.IndexOf('{');
            var lastBrace = value.LastIndexOf('}');
            return firstBrace >= 0 && lastBrace > firstBrace
                ? value.Substring(firstBrace, lastBrace - firstBrace + 1).Trim()
                : string.Empty;
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

        private string BuildPlannerKnowledgeBaseHint()
        {
            if (_knowledgeBaseSummaryCache == null)
            {
                return "not_loaded";
            }

            var projectTitles = (_knowledgeBaseSummaryCache.ProjectCards ?? Array.Empty<HostedKnowledgeBaseProjectCardDto>())
                .Take(5)
                .Select(card => card.IsRecent ? $"{card.Title} (recent)" : card.Title)
                .Where(title => !string.IsNullOrWhiteSpace(title))
                .ToArray();
            var activeProjectTitle = (_knowledgeBaseSummaryCache.ProjectCards ?? Array.Empty<HostedKnowledgeBaseProjectCardDto>())
                .FirstOrDefault(card => string.Equals(card.ProjectCardId, _activeProjectCardId, StringComparison.Ordinal))
                ?.Title;

            return string.Join(
                "\n",
                new[]
                {
                    $"status={_knowledgeBaseSummaryCache.Status}",
                    $"profile_available={HasProfileGrounding(_knowledgeBaseSummaryCache.ProfileCard)}",
                    $"project_count={_knowledgeBaseSummaryCache.ProjectCards?.Count ?? 0}",
                    $"active_project={(string.IsNullOrWhiteSpace(activeProjectTitle) ? "(none)" : activeProjectTitle)}",
                    $"project_titles={(projectTitles.Length == 0 ? "(none)" : string.Join(", ", projectTitles))}"
                });
        }

        private async Task RefreshRetrievedKnowledgeSnippetsAsync(
            string retrievalQuery,
            IReadOnlyList<string>? preferredDocumentIds,
            CancellationToken cancellationToken)
        {
            var previousSnippets = _retrievedKnowledgeSnippets;
            var canReusePreviousSnippets = preferredDocumentIds != null && preferredDocumentIds.Count > 0;
            RagTraceLogger.WriteLine(
                $"retrieval:start query='{TrimForLog(retrievalQuery, 240)}' preferred_docs={FormatDocumentIds(preferredDocumentIds)} previous_snippet_count={previousSnippets.Count}");
            var retrievedSnippets = _knowledgeRetriever == null
                ? Array.Empty<RetrievedContextSnippet>()
                : await _knowledgeRetriever(retrievalQuery, preferredDocumentIds, cancellationToken);

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
                RagTraceLogger.WriteLine("retrieval:empty preserving_previous_snippets=false");
            }

            UpdateSystemPromptWithContext();
        }

        private void ClearRetrievedKnowledgeSnippets()
        {
            if (_retrievedKnowledgeSnippets.Count == 0)
            {
                return;
            }

            _retrievedKnowledgeSnippets = Array.Empty<RetrievedContextSnippet>();
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
                    Source = "planner"
                };
            }

            public static ResponsePlan Profile(
                string knowledgeQuery,
                string target,
                RetrievalScope scope,
                double confidence,
                string source = "planner")
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
                string source = "planner")
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
            public DateTime CachedAtUtc { get; init; }
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
