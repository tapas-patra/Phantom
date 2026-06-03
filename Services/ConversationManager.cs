using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SecureOverlay.Services
{
    public class ConversationManager
    {
        private List<ConversationMessage> _fullConversation = new List<ConversationMessage>();
        private string _systemPrompt = "";
        private string _resumeText = string.Empty;
        private string _resumeSummary = string.Empty;
        private bool _resumeSummarized = false;

        // NEW: Job Description fields
        private string _jobDescriptionText = string.Empty;
        private string _jobDescriptionSummary = string.Empty;
        private bool _jobDescriptionSummarized = false;

        
        private ModelConfig _modelConfig;
        private IAIService _aiService;


        private APIRotationManager? _rotationManager;
        private string _currentProvider = "";

        public event EventHandler<string>? APISwitchNotification;

        public ConversationManager(IAIService aiService, string systemPrompt, ModelConfig modelConfig, APIRotationManager? rotationManager = null)
        {
            _aiService = aiService;
            _systemPrompt = systemPrompt;
            _modelConfig = modelConfig;
            _rotationManager = rotationManager;
            _currentProvider = aiService.GetProviderName();

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
            Log.WriteLine("═══════════════════════════════════════════════════════");
            Log.WriteLine("UpdateSystemPromptWithContext() CALLED");
            Log.WriteLine("═══════════════════════════════════════════════════════");
            
            var contextParts = new System.Text.StringBuilder();
            contextParts.Append(_systemPrompt);
            
            Log.WriteLine($"Base system prompt length: {_systemPrompt.Length} chars");
            
            if (!string.IsNullOrWhiteSpace(_resumeSummary))
            {
                Log.WriteLine($"✓ Adding resume summary ({_resumeSummary.Length} chars)");
                contextParts.Append($"\n\nUser Profile: {_resumeSummary}");
            }
            else
            {
                Log.WriteLine("✗ Resume summary is empty - NOT adding");
            }
            
            if (!string.IsNullOrWhiteSpace(_jobDescriptionSummary))
            {
                Log.WriteLine($"✓ Adding JD summary ({_jobDescriptionSummary.Length} chars)");
                Log.WriteLine($"JD Summary Preview: {_jobDescriptionSummary.Substring(0, Math.Min(100, _jobDescriptionSummary.Length))}...");
                contextParts.Append($"\n\nInterview Context: You are helping the user prepare for an interview for the following position. Provide relevant advice, practice questions, and feedback based on this job description:\n{_jobDescriptionSummary}");
            }
            else
            {
                Log.WriteLine("✗ JD summary is empty - NOT adding");
                Log.WriteLine($"   _jobDescriptionText is empty: {string.IsNullOrWhiteSpace(_jobDescriptionText ?? string.Empty)}");
                Log.WriteLine($"   _jobDescriptionSummarized: {_jobDescriptionSummarized}");
            }
            
            var finalContent = contextParts.ToString();
            
            // ✅ FIX: Check if list is empty (happens after ClearConversation)
            if (_fullConversation.Count == 0)
            {
                // Add new system message
                _fullConversation.Add(new ConversationMessage
                {
                    Role = "system",
                    Content = finalContent,
                    EstimatedTokens = EstimateTokens(finalContent)
                });
                Log.WriteLine($"✓ Created NEW system message ({finalContent.Length} chars, {EstimateTokens(finalContent)} tokens)");
            }
            else
            {
                // Update existing system message at index 0
                _fullConversation[0].Content = finalContent;
                _fullConversation[0].EstimatedTokens = EstimateTokens(finalContent);
                Log.WriteLine($"✓ Updated EXISTING system message ({finalContent.Length} chars, {EstimateTokens(finalContent)} tokens)");
            }
            
            Log.WriteLine($"Messages in conversation: {_fullConversation.Count}");
            Log.WriteLine("═══════════════════════════════════════════════════════");
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
            var (fullResponse, summary, hasCode) = ParseAIResponse(response);

            // Add AI response to full conversation
            var aiMsg = new ConversationMessage
            {
                Role = "assistant",
                Content = fullResponse,
                Summary = summary,
                HasCode = hasCode,
                EstimatedTokens = EstimateTokens(fullResponse)
            };
            _fullConversation.Add(aiMsg);

            Log.WriteLine($"✓ Response added to conversation ({_fullConversation.Count} total messages)");

            return (fullResponse, "");
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

            var (fullResponse, summary, hasCode) = ParseAIResponse(response);

            var aiMsg = new ConversationMessage
            {
                Role = "assistant",
                Content = fullResponse,
                Summary = summary,
                HasCode = hasCode,
                EstimatedTokens = EstimateTokens(fullResponse)
            };
            _fullConversation.Add(aiMsg);

            Log.WriteLine($"✓ Response added to conversation ({_fullConversation.Count} total messages)");

            return (fullResponse, "");
        }



        // ═══════════════════════════════════════════════════════════════
        // OPTIMIZED CONTEXT BUILDING (Keep last 2 pairs in full)
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

            // 3. Identify the last 2 pairs (4 messages: user + assistant + user + assistant)
            var keepFullCount = Math.Min(4, userMessages.Count); // Last 4 messages (2 pairs)
            var recentMessages = userMessages.Skip(Math.Max(0, userMessages.Count - keepFullCount)).ToList();
            
            // Reserve tokens for these recent messages (in full)
            var recentTokens = recentMessages.Sum(m => m.EstimatedTokens);
            tokenBudget -= recentTokens;

            Log.WriteLine($"Reserving {recentTokens} tokens for last {recentMessages.Count} messages (full content)");

            // 4. Build sliding window for OLDER messages (with summaries)
            var slidingWindow = new List<ConversationMessage>();
            var olderMessages = userMessages.Take(Math.Max(0, userMessages.Count - keepFullCount)).Reverse().ToList();

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

            // 6. Add the recent messages (last 2 pairs) in FULL
            context.AddRange(recentMessages);

            Log.WriteLine($"Context built: {context.Count} messages, ~{context.Sum(m => m.EstimatedTokens)} tokens");
            Log.WriteLine($"  - System: 1 message (includes resume + JD)");
            Log.WriteLine($"  - Older (summarized): {slidingWindow.Count} messages");
            Log.WriteLine($"  - Recent (full): {recentMessages.Count} messages");

            var validatedContext = ValidateAndCleanMessages(context);
            return validatedContext;
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
                    
                    Log.WriteLine($"Using: Key #{currentKeyIndex + 1}/{totalKeys}, Model: {currentModel}");
                    
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
                
                Log.WriteLine($"Using: Key #{currentKeyIndex + 1}/{totalKeys}, Model: {currentModel}");
                
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
