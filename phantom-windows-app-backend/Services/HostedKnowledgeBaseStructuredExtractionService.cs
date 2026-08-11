using System.Net.Http.Headers;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class HostedKnowledgeBaseStructuredExtractionService
{
    public const string ProfileSection = "profile";
    public const string ExperienceSection = "experience";
    public const string ProjectSection = "project";
    public const string GeneralReferenceSection = "general_reference";

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly string[] SkillKeywords =
    {
        "c#", ".net", "asp.net", "sql", "postgresql", "redis", "docker", "kubernetes",
        "azure", "aws", "gcp", "react", "javascript", "typescript", "node.js", "python",
        "java", "spring", "microservices", "grpc", "rest", "graphql", "websockets",
        "kafka", "rabbitmq", "elasticsearch", "mongodb", "mysql", "linux", "wpf",
        "webview2", "machine learning", "llm", "rag", "openai", "gemini", "claude"
    };

    private static readonly string[] DomainKeywords =
    {
        "fintech", "payments", "e-commerce", "healthcare", "education", "saas", "ai",
        "analytics", "security", "recruitment", "interview", "gaming", "social", "crm"
    };
    private static readonly HashSet<string> EvidenceStopWords = new(StringComparer.Ordinal)
    {
        "and", "for", "from", "into", "the", "that", "this", "with", "using", "worked", "built"
    };

    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly ManagedAiCatalogService _catalogService;
    private readonly SecretProtector _protector;
    private readonly HostedKnowledgeBaseRepository _knowledgeBases;

    public HostedKnowledgeBaseStructuredExtractionService(
        ManagedProviderCredentialRepository credentials,
        ManagedAiCatalogService catalogService,
        SecretProtector protector,
        HostedKnowledgeBaseRepository knowledgeBases)
    {
        _credentials = credentials;
        _catalogService = catalogService;
        _protector = protector;
        _knowledgeBases = knowledgeBases;
        RunExperienceExtractionSelfCheck();
        RunGroundingValidationSelfCheck();
    }

    public async Task<HostedKnowledgeBaseStructuredExtractionResult> ExtractAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord> documents,
        CancellationToken cancellationToken)
    {
        var profileDocuments = documents
            .Where(document => string.Equals(document.Section, ProfileSection, StringComparison.OrdinalIgnoreCase))
            .OrderBy(document => document.UploadedAtUtc)
            .ToArray();
        var projectDocuments = documents
            .Where(document => string.Equals(document.Section, ProjectSection, StringComparison.OrdinalIgnoreCase))
            .OrderBy(document => document.UploadedAtUtc)
            .ToArray();
        var experienceDocuments = documents
            .Where(document => string.Equals(document.Section, ExperienceSection, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(document => document.UploadedAtUtc)
            .ToArray();

        var existingProfile = _knowledgeBases.FindProfileCard(knowledgeBase.KnowledgeBaseId);
        var existingProjects = _knowledgeBases.ListProjectCards(knowledgeBase.KnowledgeBaseId);
        var existingExperiences = _knowledgeBases.ListExperienceCards(knowledgeBase.KnowledgeBaseId);

        var profileCard = profileDocuments.Length == 0
            ? null
            : RawSourceDocumentIds(existingProfile?.SourceDocumentIdsJson).SetEquals(profileDocuments.Select(document => document.DocumentId))
                ? existingProfile
                : await BuildProfileCardAsync(account, knowledgeBase, profileDocuments, cancellationToken);

        var projectCards = new List<HostedKnowledgeBaseProjectCardRecord>(projectDocuments.Length);
        for (var index = 0; index < projectDocuments.Length; index++)
        {
            var document = projectDocuments[index];
            var existing = existingProjects.FirstOrDefault(card => RawSourceDocumentIds(card.SourceDocumentIdsJson).Contains(document.DocumentId));
            projectCards.Add(existing ?? await BuildProjectCardAsync(account, knowledgeBase, document, index, cancellationToken));
        }

        var experienceCards = new List<HostedKnowledgeBaseExperienceCardRecord>(experienceDocuments.Length);
        var experienceSortOrder = 0;
        for (var index = 0; index < experienceDocuments.Length; index++)
        {
            var document = experienceDocuments[index];
            var existing = existingExperiences
                .Where(card => RawSourceDocumentIds(card.SourceDocumentIdsJson).Contains(document.DocumentId)
                    && card.ExperienceCardId.StartsWith($"kb-experience-{document.DocumentId}-", StringComparison.Ordinal))
                .OrderBy(card => card.SortOrder)
                .ToArray();
            if (existing.Length > 0)
            {
                experienceCards.AddRange(existing);
                experienceSortOrder += existing.Length;
                continue;
            }

            var extracted = await BuildExperienceCardsAsync(account, knowledgeBase, document, experienceSortOrder, cancellationToken);
            experienceCards.AddRange(extracted);
            experienceSortOrder += extracted.Count;
        }

        if (experienceCards.Count == 1)
        {
            experienceCards[0].IsCurrent = true;
            experienceCards[0].EndDate = string.Empty;
        }
        else if (experienceCards.Count(card => card.IsCurrent) > 1)
        {
            var currentExperience = experienceCards.First(card => card.IsCurrent);
            foreach (var card in experienceCards)
            {
                card.IsCurrent = ReferenceEquals(card, currentExperience);
            }
        }

        if (projectCards.Count > 0 && !projectCards.Any(card => card.IsRecent))
        {
            projectCards[0].IsRecent = true;
        }

        return new HostedKnowledgeBaseStructuredExtractionResult
        {
            ProfileCard = profileCard,
            ProjectCards = projectCards,
            ExperienceCards = experienceCards
        };
    }

    private async Task<HostedKnowledgeBaseProfileCardRecord> BuildProfileCardAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        IReadOnlyList<HostedKnowledgeBaseDocumentRecord> documents,
        CancellationToken cancellationToken)
    {
        var combinedText = string.Join(
            "\n\n",
            documents.Select(document => $"[Source: {document.SourceLabelOrFileName()}]\n{document.ExtractedText}".Trim()));
        var cleanedText = NormalizeProfileSourceText(combinedText);
        var fallback = ExtractProfileFallback(cleanedText);
        var llm = ValidateProfileExtraction(
            await TryExtractProfileWithLlmAsync(account, cleanedText, cancellationToken),
            cleanedText);
        var extracted = MergeProfileExtraction(llm, fallback);
        var now = DateTime.UtcNow;

        return new HostedKnowledgeBaseProfileCardRecord
        {
            ProfileCardId = $"kb-profile-{knowledgeBase.KnowledgeBaseId}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = knowledgeBase.UserId,
            FullName = extracted.FullName,
            ResumeText = extracted.CandidateInfo,
            ShortIntro = extracted.ShortIntro,
            CurrentRole = extracted.CurrentRole,
            YearsOfExperience = extracted.YearsOfExperience,
            StrengthsJson = SerializeStringList(extracted.Strengths),
            SkillsJson = SerializeStringList(extracted.Skills),
            DomainsJson = SerializeStringList(extracted.Domains),
            SourceDocumentIdsJson = SerializeStringList(documents.Select(document => document.DocumentId)),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private async Task<HostedKnowledgeBaseProjectCardRecord> BuildProjectCardAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        HostedKnowledgeBaseDocumentRecord document,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        var fallback = ExtractProjectFallback(document, sortOrder);
        var extracted = MergeProjectExtraction(
            ValidateProjectExtraction(
                await TryExtractProjectWithLlmAsync(account, document, cancellationToken),
                document.ExtractedText),
            fallback);
        var now = DateTime.UtcNow;
        var title = string.IsNullOrWhiteSpace(extracted.Title)
            ? document.SourceLabelOrFileName()
            : extracted.Title;

        return new HostedKnowledgeBaseProjectCardRecord
        {
            ProjectCardId = $"kb-project-{document.DocumentId}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = knowledgeBase.UserId,
            Title = title,
            Slug = Slugify(title),
            IsRecent = extracted.IsRecent,
            SortOrder = sortOrder,
            Role = extracted.Role,
            Summary = extracted.Summary,
            StackJson = SerializeStringList(extracted.Stack),
            Architecture = extracted.Architecture,
            Challenges = extracted.Challenges,
            Impact = extracted.Impact,
            SourceDocumentIdsJson = SerializeStringList(new[] { document.DocumentId }),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private async Task<IReadOnlyList<HostedKnowledgeBaseExperienceCardRecord>> BuildExperienceCardsAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseRecord knowledgeBase,
        HostedKnowledgeBaseDocumentRecord document,
        int sortOrder,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ExperienceExtractionResult> extracted = (await TryExtractExperiencesWithLlmAsync(account, document, cancellationToken))
            .Select(item => ValidateExperienceExtraction(item, document.ExtractedText))
            .Where(item => !string.IsNullOrWhiteSpace(item.Company) || !string.IsNullOrWhiteSpace(item.Role))
            .ToArray();
        var fallback = ExtractExperienceFallbacks(document, sortOrder);
        if (extracted.Count == 0)
        {
            extracted = fallback;
        }
        else
        {
            extracted = extracted.Select((item, index) => MergeExperienceExtraction(
                    item,
                    index < fallback.Count ? fallback[index] : null))
                .Concat(fallback.Skip(extracted.Count))
                .ToArray();
        }

        var now = DateTime.UtcNow;
        return extracted.Select((experience, index) => new HostedKnowledgeBaseExperienceCardRecord
        {
            ExperienceCardId = $"kb-experience-{document.DocumentId}-{index}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = knowledgeBase.UserId,
            Company = experience.Company,
            Role = experience.Role,
            IsCurrent = experience.IsCurrent,
            SortOrder = sortOrder + index,
            StartDate = NormalizeExperienceDate(experience.StartDate),
            EndDate = experience.IsCurrent ? string.Empty : NormalizeExperienceDate(experience.EndDate),
            Summary = experience.Summary,
            Responsibilities = experience.Responsibilities,
            SkillsJson = SerializeStringList(experience.Skills),
            SourceDocumentIdsJson = SerializeStringList(new[] { document.DocumentId }),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        }).ToArray();
    }

    private static ExperienceExtractionResult MergeExperienceExtraction(
        ExperienceExtractionResult extracted,
        ExperienceExtractionResult? fallback)
    {
        if (fallback == null)
        {
            return extracted;
        }

        return new ExperienceExtractionResult
        {
            Company = FirstNonEmpty(extracted.Company, fallback.Company),
            Role = FirstNonEmpty(extracted.Role, fallback.Role),
            IsCurrent = extracted.IsCurrent || fallback.IsCurrent,
            StartDate = FirstNonEmpty(extracted.StartDate, fallback.StartDate),
            EndDate = FirstNonEmpty(extracted.EndDate, fallback.EndDate),
            Summary = FirstNonEmpty(extracted.Summary, fallback.Summary),
            Responsibilities = FirstNonEmpty(extracted.Responsibilities, fallback.Responsibilities),
            Skills = MergeNonEmpty(extracted.Skills, fallback.Skills)
        };
    }

    private async Task<ProfileExtractionResult?> TryExtractProfileWithLlmAsync(
        DesktopAccountRecord account,
        string sourceText,
        CancellationToken cancellationToken)
    {
        var prompt = """
Return strict JSON only with this shape:
{"fullName":"","candidateInfo":"","shortIntro":"","currentRole":"","yearsOfExperience":0,"strengths":[""],"skills":[""],"domains":[""]}

Extract a grounded candidate profile from the source text.
Rules:
- Use only source text.
- candidateInfo may include education, certifications, location, preferences, and other personal context, but must exclude employment history and role responsibilities.
- Keep short_intro to 2-4 sentences.
- skills and domains must be concise arrays.
- If unknown, use empty string, empty array, or 0.
""";

        var json = await TryCompleteJsonAsync(account, prompt, sourceText, cancellationToken);
        return TryDeserialize<ProfileExtractionResult>(json);
    }

    private async Task<ProjectExtractionResult?> TryExtractProjectWithLlmAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseDocumentRecord document,
        CancellationToken cancellationToken)
    {
        var prompt = $@"Return strict JSON only with this shape:
{{""title"":"""",""role"":"""",""summary"":"""",""stack"":[""""],""architecture"":"""",""challenges"":"""",""impact"":"""",""is_recent"":false}}

Extract a grounded project card from the source text.
Rules:
- Use only source text.
- Keep summary concise and interview-ready.
- stack must be a concise array.
- If the source does not explicitly say it is recent, set is_recent false.
- If unknown, use empty string, empty array, or false.

Source label: {document.SourceLabelOrFileName()}";

        var json = await TryCompleteJsonAsync(account, prompt, document.ExtractedText, cancellationToken);
        return TryDeserialize<ProjectExtractionResult>(json);
    }

    private async Task<IReadOnlyList<ExperienceExtractionResult>> TryExtractExperiencesWithLlmAsync(
        DesktopAccountRecord account,
        HostedKnowledgeBaseDocumentRecord document,
        CancellationToken cancellationToken)
    {
        var prompt = """
Return strict JSON only with this shape:
{"experiences":[{"company":"","role":"","isCurrent":false,"startDate":"YYYY-MM","endDate":"YYYY-MM","summary":"","responsibilities":"","skills":[""]}]}

Extract every distinct employment experience from the source text. One company/role period must produce one array item.
Rules:
- Use only source text and do not mix responsibilities from another role.
- Set isCurrent only when the role is explicitly current or present.
- Normalize dates to YYYY-MM. For present/current roles use an empty endDate.
- Company and role are required; omit entries where neither can be grounded.
- Keep responsibilities detailed enough to answer day-to-day interview questions.
- If unknown, use empty string, empty array, or false.
""";

        var json = await TryCompleteJsonAsync(account, prompt, document.ExtractedText, cancellationToken);
        return TryDeserialize<ExperienceExtractionEnvelope>(json)?.Experiences?
            .Where(item => !string.IsNullOrWhiteSpace(item.Company) || !string.IsNullOrWhiteSpace(item.Role))
            .ToArray()
            ?? Array.Empty<ExperienceExtractionResult>();
    }

    public async Task<string?> TryCompleteJsonAsync(
        DesktopAccountRecord account,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var catalog = _catalogService.GetCatalogForAccount(account);
        foreach (var provider in catalog.Providers)
        {
            var model = provider.Models.FirstOrDefault();
            if (model == null)
            {
                continue;
            }

            var credential = _credentials.ListByProvider(provider.ProviderId)
                .Where(item => item.IsEnabled)
                .OrderBy(item => item.Priority)
                .ThenByDescending(item => item.UpdatedAtUtc)
                .FirstOrDefault();
            if (credential == null)
            {
                continue;
            }

            try
            {
                var apiKey = _protector.Unprotect(credential.EncryptedApiKey);
                return await CompleteOnceAsync(provider.ProviderId, model.ModelId, apiKey, systemPrompt, userPrompt, cancellationToken);
            }
            catch
            {
            }
        }

        return null;
    }

    private static async Task<string> CompleteOnceAsync(
        string providerId,
        string modelId,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        return providerId switch
        {
            ManagedAiCatalog.ChatGpt => await CompleteOpenAiCompatibleAsync(
                "https://api.openai.com/v1/chat/completions",
                modelId,
                apiKey,
                systemPrompt,
                userPrompt,
                cancellationToken),
            ManagedAiCatalog.Mistral => await CompleteOpenAiCompatibleAsync(
                "https://api.mistral.ai/v1/chat/completions",
                modelId,
                apiKey,
                systemPrompt,
                userPrompt,
                cancellationToken),
            ManagedAiCatalog.Groq => await CompleteOpenAiCompatibleAsync(
                "https://api.groq.com/openai/v1/chat/completions",
                modelId,
                apiKey,
                systemPrompt,
                userPrompt,
                cancellationToken),
            ManagedAiCatalog.Nvidia => await CompleteOpenAiCompatibleAsync(
                "https://integrate.api.nvidia.com/v1/chat/completions",
                modelId,
                apiKey,
                systemPrompt,
                userPrompt,
                cancellationToken),
            ManagedAiCatalog.Claude => await CompleteClaudeAsync(modelId, apiKey, systemPrompt, userPrompt, cancellationToken),
            ManagedAiCatalog.Gemini => await CompleteGeminiAsync(modelId, apiKey, systemPrompt, userPrompt, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported provider '{providerId}'.")
        };
    }

    private static async Task<string> CompleteOpenAiCompatibleAsync(
        string url,
        string modelId,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = modelId,
            temperature = 0.1,
            max_tokens = 1200,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? string.Empty;
    }

    private static async Task<string> CompleteClaudeAsync(
        string modelId,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = modelId,
            max_tokens = 1200,
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = userPrompt }
            }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static async Task<string> CompleteGeminiAsync(
        string modelId,
        string apiKey,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { text = $"{systemPrompt}\n\n{userPrompt}" }
                    }
                }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 1200
            }
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelId)}:generateContent?key={Uri.EscapeDataString(apiKey)}");
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? string.Empty;
    }

    private static T? TryDeserialize<T>(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        var json = ExtractJsonObject(value);
        if (string.IsNullOrWhiteSpace(json))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return default;
        }
    }

    private static string ExtractJsonObject(string value)
    {
        var fencedMatch = Regex.Match(value, "```(?:json)?\\s*(\\{.*\\})\\s*```", RegexOptions.IgnoreCase | RegexOptions.Singleline);
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

    private static ProfileExtractionResult ExtractProfileFallback(string sourceText)
    {
        var lines = NormalizeLines(sourceText);
        var meaningfulLines = lines
            .Where(line => !IsContactOrNoiseLine(line))
            .ToArray();
        var fullName = meaningfulLines.FirstOrDefault(IsLikelyPersonName);
        var currentRole = meaningfulLines
            .SkipWhile(line => string.Equals(line, fullName, StringComparison.Ordinal))
            .FirstOrDefault(IsLikelyRoleLine);
        var yearsMatch = Regex.Match(sourceText, @"(\d{1,2})\s*\+?\s*(?:years?|yrs?)", RegexOptions.IgnoreCase);
        var skills = ExtractKeywords(sourceText, SkillKeywords, 10);
        var domains = ExtractKeywords(sourceText, DomainKeywords, 5);
        var intro = BuildProfileIntro(fullName, currentRole, yearsMatch, skills, domains, meaningfulLines);

        return new ProfileExtractionResult
        {
            FullName = fullName ?? string.Empty,
            CandidateInfo = string.Empty,
            ShortIntro = intro,
            CurrentRole = currentRole ?? string.Empty,
            YearsOfExperience = yearsMatch.Success && int.TryParse(yearsMatch.Groups[1].Value, out var years) ? years : 0,
            Strengths = ExtractBulletLikeLines(sourceText, 5),
            Skills = skills,
            Domains = domains
        };
    }

    private static ProfileExtractionResult MergeProfileExtraction(
        ProfileExtractionResult? llm,
        ProfileExtractionResult fallback)
    {
        if (llm == null)
        {
            return fallback;
        }

        return new ProfileExtractionResult
        {
            FullName = FirstNonEmpty(llm.FullName, fallback.FullName),
            CandidateInfo = FirstNonEmpty(llm.CandidateInfo, fallback.CandidateInfo),
            ShortIntro = FirstNonEmpty(llm.ShortIntro, fallback.ShortIntro),
            CurrentRole = FirstNonEmpty(llm.CurrentRole, fallback.CurrentRole),
            YearsOfExperience = llm.YearsOfExperience > 0 ? llm.YearsOfExperience : fallback.YearsOfExperience,
            Strengths = MergeNonEmpty(llm.Strengths, fallback.Strengths),
            Skills = MergeNonEmpty(llm.Skills, fallback.Skills),
            Domains = MergeNonEmpty(llm.Domains, fallback.Domains)
        };
    }

    private static ProfileExtractionResult? ValidateProfileExtraction(ProfileExtractionResult? value, string source)
    {
        if (value == null) return null;
        return new ProfileExtractionResult
        {
            FullName = Grounded(value.FullName, source),
            CandidateInfo = Grounded(value.CandidateInfo, source),
            ShortIntro = Grounded(value.ShortIntro, source),
            CurrentRole = Grounded(value.CurrentRole, source),
            YearsOfExperience = Regex.IsMatch(source, $@"\b{value.YearsOfExperience}\s*\+?\s*(?:years?|yrs?)\b", RegexOptions.IgnoreCase)
                ? value.YearsOfExperience
                : 0,
            Strengths = Grounded(value.Strengths, source),
            Skills = Grounded(value.Skills, source),
            Domains = Grounded(value.Domains, source)
        };
    }

    private static ProjectExtractionResult MergeProjectExtraction(ProjectExtractionResult? value, ProjectExtractionResult fallback)
    {
        if (value == null) return fallback;
        return new ProjectExtractionResult
        {
            Title = FirstNonEmpty(value.Title, fallback.Title),
            Role = FirstNonEmpty(value.Role, fallback.Role),
            Summary = FirstNonEmpty(value.Summary, fallback.Summary),
            Stack = MergeNonEmpty(value.Stack, fallback.Stack),
            Architecture = FirstNonEmpty(value.Architecture, fallback.Architecture),
            Challenges = FirstNonEmpty(value.Challenges, fallback.Challenges),
            Impact = FirstNonEmpty(value.Impact, fallback.Impact),
            IsRecent = value.IsRecent || fallback.IsRecent
        };
    }

    private static ProjectExtractionResult? ValidateProjectExtraction(ProjectExtractionResult? value, string source)
    {
        if (value == null) return null;
        return new ProjectExtractionResult
        {
            Title = Grounded(value.Title, source),
            Role = Grounded(value.Role, source),
            Summary = Grounded(value.Summary, source),
            Stack = Grounded(value.Stack, source),
            Architecture = Grounded(value.Architecture, source),
            Challenges = Grounded(value.Challenges, source),
            Impact = Grounded(value.Impact, source),
            IsRecent = value.IsRecent && Regex.IsMatch(source, @"\b(recent|latest|current)\b", RegexOptions.IgnoreCase)
        };
    }

    private static ExperienceExtractionResult ValidateExperienceExtraction(ExperienceExtractionResult value, string source)
    {
        return new ExperienceExtractionResult
        {
            Company = Grounded(value.Company, source),
            Role = Grounded(value.Role, source),
            IsCurrent = value.IsCurrent && Regex.IsMatch(source, @"\b(current|present|now)\b", RegexOptions.IgnoreCase),
            StartDate = Grounded(value.StartDate, source),
            EndDate = Grounded(value.EndDate, source),
            Summary = Grounded(value.Summary, source),
            Responsibilities = Grounded(value.Responsibilities, source),
            Skills = Grounded(value.Skills, source)
        };
    }

    private static string Grounded(string? claim, string source)
        => IsGrounded(claim, source) ? claim!.Trim() : string.Empty;

    private static IReadOnlyList<string> Grounded(IReadOnlyList<string>? claims, string source)
        => (claims ?? Array.Empty<string>())
            .Where(claim => IsGrounded(claim, source))
            .Select(claim => claim.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsGrounded(string? claim, string source)
    {
        if (string.IsNullOrWhiteSpace(claim)) return false;
        var normalizedClaim = NormalizeEvidence(claim);
        var normalizedSource = NormalizeEvidence(source);
        if (normalizedSource.Contains(normalizedClaim, StringComparison.Ordinal)) return true;
        var tokens = normalizedClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !EvidenceStopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (tokens.Length == 0) return false;
        var sourceTokens = normalizedSource.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var matches = tokens.Count(sourceTokens.Contains);
        return matches >= Math.Min(2, tokens.Length) && matches / (double)tokens.Length >= 0.90d;
    }

    private static string NormalizeEvidence(string? value)
        => Regex.Replace((value ?? string.Empty).ToLowerInvariant(), @"[^a-z0-9+#.]+", " ").Trim();

    private static ProjectExtractionResult ExtractProjectFallback(HostedKnowledgeBaseDocumentRecord document, int sortOrder)
    {
        var lines = NormalizeLines(document.ExtractedText);
        var title = document.SourceLabelOrFileName();
        var summary = SummarizeText(document.ExtractedText, 420);
        return new ProjectExtractionResult
        {
            Title = lines.FirstOrDefault(line => line.Length > 3 && line.Length <= 90) ?? title,
            Role = FindSectionValue(document.ExtractedText, "role"),
            Summary = summary,
            Stack = ExtractKeywords(document.ExtractedText, SkillKeywords, 8),
            Architecture = FindSectionValue(document.ExtractedText, "architecture"),
            Challenges = FindSectionValue(document.ExtractedText, "challenge", "challenges"),
            Impact = FindSectionValue(document.ExtractedText, "impact", "result", "results"),
            IsRecent = sortOrder == 0
        };
    }

    private static IReadOnlyList<ExperienceExtractionResult> ExtractExperienceFallbacks(
        HostedKnowledgeBaseDocumentRecord document,
        int sortOrder)
    {
        var lines = NormalizeLines(document.ExtractedText);
        var datePattern = new Regex(
            @"(?<start>(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}|(?:19|20)\d{2}[-/]\d{1,2}|\d{1,2}/(?:19|20)\d{2})\s*(?:-|–|—|to)\s*(?<end>present|current|now|(?:(?:Jan(?:uary)?|Feb(?:ruary)?|Mar(?:ch)?|Apr(?:il)?|May|Jun(?:e)?|Jul(?:y)?|Aug(?:ust)?|Sep(?:tember)?|Oct(?:ober)?|Nov(?:ember)?|Dec(?:ember)?)\s+)?(?:19|20)\d{2}|(?:19|20)\d{2}[-/]\d{1,2}|\d{1,2}/(?:19|20)\d{2})",
            RegexOptions.IgnoreCase);
        var datedLines = lines
            .Select((line, index) => new { Line = line, Index = index, Match = datePattern.Match(line) })
            .Where(item => item.Match.Success)
            .ToArray();
        if (datedLines.Length == 0)
        {
            var role = FirstNonEmpty(
                FindSectionValue(document.ExtractedText, "role", "title", "position"),
                lines.FirstOrDefault(IsLikelyRoleLine) ?? string.Empty);
            var company = FirstNonEmpty(
                FindSectionValue(document.ExtractedText, "company", "employer"),
                document.SourceLabelOrFileName());
            return new[]
            {
                new ExperienceExtractionResult
                {
                    Company = company,
                    Role = role,
                    IsCurrent = Regex.IsMatch(document.ExtractedText, @"\b(current|present)\b", RegexOptions.IgnoreCase),
                    StartDate = FindSectionValue(document.ExtractedText, "start", "start date"),
                    EndDate = FindSectionValue(document.ExtractedText, "end", "end date"),
                    Summary = SummarizeText(document.ExtractedText, 420),
                    Responsibilities = FirstNonEmpty(
                        FindSectionValue(document.ExtractedText, "responsibilities", "day-to-day", "duties"),
                        string.Join("\n", ExtractBulletLikeLines(document.ExtractedText, 12))),
                    Skills = ExtractKeywords(document.ExtractedText, SkillKeywords, 12)
                }
            };
        }

        var results = new List<ExperienceExtractionResult>(datedLines.Length);
        for (var itemIndex = 0; itemIndex < datedLines.Length; itemIndex++)
        {
            var datedLine = datedLines[itemIndex];
            var heading = datedLine.Line[..datedLine.Match.Index].Trim(' ', '|', ',', '-', '–', '—');
            var previous = datedLine.Index > 0 ? lines[datedLine.Index - 1] : string.Empty;
            var previousTwo = datedLine.Index > 1 ? lines[datedLine.Index - 2] : string.Empty;
            var (company, role) = ParseCompanyAndRole(heading, previous, previousTwo);
            var nextDateIndex = itemIndex + 1 < datedLines.Length ? datedLines[itemIndex + 1].Index : lines.Count;
            var responsibilityEnd = nextDateIndex;
            if (itemIndex + 1 < datedLines.Length
                && datedLines[itemIndex + 1].Match.Index == 0
                && responsibilityEnd - datedLine.Index > 2)
            {
                responsibilityEnd = Math.Max(datedLine.Index + 1, responsibilityEnd - 2);
            }
            var responsibilities = string.Join("\n", lines
                .Skip(datedLine.Index + 1)
                .Take(Math.Max(0, responsibilityEnd - datedLine.Index - 1))
                .Select(line => line.TrimStart('-', '*', '•', ' '))
                .Where(line => !string.IsNullOrWhiteSpace(line)));
            var endText = datedLine.Match.Groups["end"].Value;
            var isCurrent = Regex.IsMatch(endText, @"^(present|current|now)$", RegexOptions.IgnoreCase);
            results.Add(new ExperienceExtractionResult
            {
                Company = company,
                Role = role,
                IsCurrent = isCurrent,
                StartDate = datedLine.Match.Groups["start"].Value,
                EndDate = isCurrent ? string.Empty : endText,
                Summary = SummarizeText(responsibilities, 420),
                Responsibilities = responsibilities,
                Skills = ExtractKeywords(responsibilities, SkillKeywords, 12)
            });
        }

        return results
            .Where(item => !string.IsNullOrWhiteSpace(item.Company) || !string.IsNullOrWhiteSpace(item.Role))
            .ToArray();
    }

    [Conditional("DEBUG")]
    private static void RunExperienceExtractionSelfCheck()
    {
        var extracted = ExtractExperienceFallbacks(new HostedKnowledgeBaseDocumentRecord
        {
            FileName = "experience.txt",
            SourceLabel = "Work history",
            ExtractedText = """
Acme Payments
Senior QA Engineer
Jan 2022 - Present
- Owned API automation and service testing.
Legacy Labs
UI Automation Engineer
Feb 2019 - Dec 2021
- Built Selenium UI automation.
"""
        }, 0);
        if (extracted.Count != 2
            || extracted[0].Company != "Acme Payments"
            || extracted[0].Role != "Senior QA Engineer"
            || !extracted[0].IsCurrent
            || extracted[1].Company != "Legacy Labs"
            || extracted[1].Role != "UI Automation Engineer"
            || extracted[1].IsCurrent
            || NormalizeExperienceDate(extracted[0].StartDate) != "2022-01")
        {
            throw new InvalidOperationException("Experience extraction self-check failed.");
        }
    }

    [Conditional("DEBUG")]
    private static void RunGroundingValidationSelfCheck()
    {
        const string source = "Built payment retries with C# and PostgreSQL for Acme Payments.";
        if (!IsGrounded("payment retries with PostgreSQL", source)
            || IsGrounded("payment retries with Kubernetes", source)
            || IsGrounded("designed Kubernetes autoscaling for healthcare workloads", source))
        {
            throw new InvalidOperationException("Structured grounding validation self-check failed.");
        }
    }

    private static (string Company, string Role) ParseCompanyAndRole(
        string heading,
        string previous,
        string previousTwo)
    {
        var atMatch = Regex.Match(heading, @"^(?<role>.+?)\s+at\s+(?<company>.+)$", RegexOptions.IgnoreCase);
        if (atMatch.Success)
        {
            return (atMatch.Groups["company"].Value.Trim(), atMatch.Groups["role"].Value.Trim());
        }

        var parts = heading.Split(new[] { '|', '•', '—' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            return IsLikelyRoleLine(parts[0])
                ? (parts[1], parts[0])
                : (parts[0], parts[1]);
        }

        if (!string.IsNullOrWhiteSpace(heading))
        {
            return IsLikelyRoleLine(heading)
                ? (previous, heading)
                : (heading, IsLikelyRoleLine(previous) ? previous : previousTwo);
        }

        if (IsLikelyRoleLine(previous))
        {
            return (previousTwo, previous);
        }

        return IsLikelyRoleLine(previousTwo)
            ? (previous, previousTwo)
            : (previousTwo, previous);
    }

    private static string NormalizeExperienceDate(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || Regex.IsMatch(normalized, @"^(present|current|now)$", RegexOptions.IgnoreCase))
        {
            return string.Empty;
        }

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }

        var yearMonth = Regex.Match(normalized, @"^(?<year>(?:19|20)\d{2})[-/](?<month>\d{1,2})$");
        if (yearMonth.Success)
        {
            return $"{yearMonth.Groups["year"].Value}-{int.Parse(yearMonth.Groups["month"].Value, CultureInfo.InvariantCulture):00}";
        }

        var monthYear = Regex.Match(normalized, @"^(?<month>\d{1,2})/(?<year>(?:19|20)\d{2})$");
        if (monthYear.Success)
        {
            return $"{monthYear.Groups["year"].Value}-{int.Parse(monthYear.Groups["month"].Value, CultureInfo.InvariantCulture):00}";
        }

        return Regex.IsMatch(normalized, @"^(?:19|20)\d{2}$") ? $"{normalized}-01" : string.Empty;
    }

    private static IReadOnlyList<string> NormalizeLines(string value)
    {
        return value.Split('\n')
            .Select(line => Regex.Replace(line ?? string.Empty, "\\s+", " ").Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
    }

    private static string NormalizeProfileSourceText(string value)
    {
        return string.Join(
            "\n",
            value.Split('\n')
                .Select(line => line.Trim())
                .Where(line => !line.StartsWith("[Source:", StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> ExtractKeywords(string sourceText, IEnumerable<string> candidates, int limit)
    {
        return candidates
            .Where(keyword => sourceText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    private static List<string> ExtractBulletLikeLines(string sourceText, int limit)
    {
        return sourceText.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("- ") || line.StartsWith("* ") || line.StartsWith("• "))
            .Select(line => line[2..].Trim())
            .Where(line => line.Length > 0)
            .Take(limit)
            .ToList();
    }

    private static string FindSectionValue(string sourceText, params string[] headings)
    {
        var lines = sourceText.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (headings.Any(heading => line.StartsWith(heading, StringComparison.OrdinalIgnoreCase)))
            {
                var value = line.Split(':', 2);
                if (value.Length == 2 && !string.IsNullOrWhiteSpace(value[1]))
                {
                    return value[1].Trim();
                }

                if (index + 1 < lines.Length && !string.IsNullOrWhiteSpace(lines[index + 1]))
                {
                    return lines[index + 1].Trim();
                }
            }
        }

        return string.Empty;
    }

    private static string SummarizeText(string sourceText, int maxLength)
    {
        var normalized = Regex.Replace(sourceText ?? string.Empty, "\\s+", " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        var slice = normalized[..maxLength];
        var lastPeriod = slice.LastIndexOf('.');
        return lastPeriod > 80 ? slice[..(lastPeriod + 1)].Trim() : slice.Trim() + "...";
    }

    private static string BuildProfileIntro(
        string? fullName,
        string? currentRole,
        Match yearsMatch,
        IReadOnlyList<string> skills,
        IReadOnlyList<string> domains,
        IReadOnlyList<string> meaningfulLines)
    {
        var yearsText = yearsMatch.Success ? yearsMatch.Groups[1].Value.Trim() : string.Empty;
        if (!string.IsNullOrWhiteSpace(currentRole))
        {
            var intro = new StringBuilder();
            intro.Append(string.IsNullOrWhiteSpace(fullName) ? "Candidate" : fullName);
            intro.Append(" is ");
            intro.Append(currentRole.Trim());
            if (!string.IsNullOrWhiteSpace(yearsText))
            {
                intro.Append($" with {yearsText}+ years of experience");
            }

            if (skills.Count > 0)
            {
                intro.Append($". Core skills include {string.Join(", ", skills.Take(5))}");
            }

            if (domains.Count > 0)
            {
                intro.Append($". Domain experience includes {string.Join(", ", domains.Take(3))}");
            }

            return intro.ToString().Trim();
        }

        return meaningfulLines.Count > 0
            ? SummarizeText(string.Join(" ", meaningfulLines.Take(6)), 320)
            : string.Empty;
    }

    private static bool IsLikelyPersonName(string line)
    {
        if (line.Length < 4 || line.Length > 60 || line.Contains('@') || Regex.IsMatch(line, @"\d"))
        {
            return false;
        }

        if (line.Contains("resume", StringComparison.OrdinalIgnoreCase)
            || line.Contains("summary", StringComparison.OrdinalIgnoreCase)
            || line.Contains("experience", StringComparison.OrdinalIgnoreCase)
            || line.Contains("skills", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 2 and <= 5;
    }

    private static bool IsLikelyRoleLine(string line)
    {
        if (line.Length < 4 || line.Length > 120 || IsContactOrNoiseLine(line))
        {
            return false;
        }

        return line.Contains("engineer", StringComparison.OrdinalIgnoreCase)
            || line.Contains("developer", StringComparison.OrdinalIgnoreCase)
            || line.Contains("qa", StringComparison.OrdinalIgnoreCase)
            || line.Contains("automation", StringComparison.OrdinalIgnoreCase)
            || line.Contains("architect", StringComparison.OrdinalIgnoreCase)
            || line.Contains("lead", StringComparison.OrdinalIgnoreCase)
            || line.Contains("manager", StringComparison.OrdinalIgnoreCase)
            || line.Contains("analyst", StringComparison.OrdinalIgnoreCase)
            || line.Contains("specialist", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContactOrNoiseLine(string line)
    {
        return line.Contains('@')
            || line.Contains("linkedin", StringComparison.OrdinalIgnoreCase)
            || line.Contains("github", StringComparison.OrdinalIgnoreCase)
            || line.Contains("http", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(line, @"^\+?[\d\-\(\)\s]{7,}$");
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static IReadOnlyList<string> MergeNonEmpty(IReadOnlyList<string>? primary, IReadOnlyList<string>? fallback)
    {
        var merged = (primary ?? Array.Empty<string>())
            .Concat(fallback ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return merged;
    }

    private static string SerializeStringList(IEnumerable<string> items)
    {
        return JsonSerializer.Serialize(
            items.Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static HashSet<string> RawSourceDocumentIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>())
                .Where(id => !string.IsNullOrWhiteSpace(id) && !id.StartsWith("kb-card-sync-", StringComparison.Ordinal))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    private static string Slugify(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    private sealed class ProfileExtractionResult
    {
        public string FullName { get; set; } = string.Empty;
        public string CandidateInfo { get; set; } = string.Empty;
        public string ShortIntro { get; set; } = string.Empty;
        public string CurrentRole { get; set; } = string.Empty;
        public int YearsOfExperience { get; set; }
        public IReadOnlyList<string> Strengths { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> Domains { get; set; } = Array.Empty<string>();
    }

    private sealed class ProjectExtractionResult
    {
        public string Title { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public IReadOnlyList<string> Stack { get; set; } = Array.Empty<string>();
        public string Architecture { get; set; } = string.Empty;
        public string Challenges { get; set; } = string.Empty;
        public string Impact { get; set; } = string.Empty;
        public bool IsRecent { get; set; }
    }

    private sealed class ExperienceExtractionResult
    {
        public string Company { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }
        public string StartDate { get; set; } = string.Empty;
        public string EndDate { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Responsibilities { get; set; } = string.Empty;
        public IReadOnlyList<string> Skills { get; set; } = Array.Empty<string>();
    }

    private sealed class ExperienceExtractionEnvelope
    {
        public IReadOnlyList<ExperienceExtractionResult> Experiences { get; set; }
            = Array.Empty<ExperienceExtractionResult>();
    }
}

public sealed class HostedKnowledgeBaseStructuredExtractionResult
{
    public HostedKnowledgeBaseProfileCardRecord? ProfileCard { get; init; }
    public IReadOnlyList<HostedKnowledgeBaseProjectCardRecord> ProjectCards { get; init; }
        = Array.Empty<HostedKnowledgeBaseProjectCardRecord>();
    public IReadOnlyList<HostedKnowledgeBaseExperienceCardRecord> ExperienceCards { get; init; }
        = Array.Empty<HostedKnowledgeBaseExperienceCardRecord>();
}

internal static class HostedKnowledgeBaseDocumentRecordExtensions
{
    public static string SourceLabelOrFileName(this HostedKnowledgeBaseDocumentRecord document)
    {
        return string.IsNullOrWhiteSpace(document.SourceLabel)
            ? document.FileName
            : document.SourceLabel;
    }
}
