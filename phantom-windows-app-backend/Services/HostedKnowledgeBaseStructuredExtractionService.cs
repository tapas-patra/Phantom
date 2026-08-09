using System.Net.Http.Headers;
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

    private readonly ManagedProviderCredentialRepository _credentials;
    private readonly ManagedAiCatalogService _catalogService;
    private readonly SecretProtector _protector;

    public HostedKnowledgeBaseStructuredExtractionService(
        ManagedProviderCredentialRepository credentials,
        ManagedAiCatalogService catalogService,
        SecretProtector protector)
    {
        _credentials = credentials;
        _catalogService = catalogService;
        _protector = protector;
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

        var profileCard = profileDocuments.Length == 0
            ? null
            : await BuildProfileCardAsync(account, knowledgeBase, profileDocuments, cancellationToken);

        var projectCards = new List<HostedKnowledgeBaseProjectCardRecord>(projectDocuments.Length);
        for (var index = 0; index < projectDocuments.Length; index++)
        {
            projectCards.Add(await BuildProjectCardAsync(account, knowledgeBase, projectDocuments[index], index, cancellationToken));
        }

        if (projectCards.Count > 0 && !projectCards.Any(card => card.IsRecent))
        {
            projectCards[0].IsRecent = true;
        }

        return new HostedKnowledgeBaseStructuredExtractionResult
        {
            ProfileCard = profileCard,
            ProjectCards = projectCards
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
        var llm = await TryExtractProfileWithLlmAsync(account, cleanedText, cancellationToken);
        var extracted = MergeProfileExtraction(llm, fallback);
        var now = DateTime.UtcNow;

        return new HostedKnowledgeBaseProfileCardRecord
        {
            ProfileCardId = $"kb-profile-{knowledgeBase.KnowledgeBaseId}",
            KnowledgeBaseId = knowledgeBase.KnowledgeBaseId,
            UserId = knowledgeBase.UserId,
            FullName = extracted.FullName,
            ResumeText = extracted.ResumeText,
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
        var extracted = await TryExtractProjectWithLlmAsync(account, document, cancellationToken)
            ?? ExtractProjectFallback(document, sortOrder);
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

    private async Task<ProfileExtractionResult?> TryExtractProfileWithLlmAsync(
        DesktopAccountRecord account,
        string sourceText,
        CancellationToken cancellationToken)
    {
        var prompt = """
Return strict JSON only with this shape:
{"full_name":"","resume_text":"","short_intro":"","current_role":"","years_of_experience":0,"strengths":[""],"skills":[""],"domains":[""]}

Extract a grounded candidate profile from the source text.
Rules:
- Use only source text.
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
            ResumeText = sourceText.Trim(),
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
            ResumeText = FirstNonEmpty(llm.ResumeText, fallback.ResumeText),
            ShortIntro = FirstNonEmpty(llm.ShortIntro, fallback.ShortIntro),
            CurrentRole = FirstNonEmpty(llm.CurrentRole, fallback.CurrentRole),
            YearsOfExperience = llm.YearsOfExperience > 0 ? llm.YearsOfExperience : fallback.YearsOfExperience,
            Strengths = MergeNonEmpty(llm.Strengths, fallback.Strengths),
            Skills = MergeNonEmpty(llm.Skills, fallback.Skills),
            Domains = MergeNonEmpty(llm.Domains, fallback.Domains)
        };
    }

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

    private static string Slugify(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "project" : normalized;
    }

    private sealed class ProfileExtractionResult
    {
        public string FullName { get; set; } = string.Empty;
        public string ResumeText { get; set; } = string.Empty;
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
}

public sealed class HostedKnowledgeBaseStructuredExtractionResult
{
    public HostedKnowledgeBaseProfileCardRecord? ProfileCard { get; init; }
    public IReadOnlyList<HostedKnowledgeBaseProjectCardRecord> ProjectCards { get; init; }
        = Array.Empty<HostedKnowledgeBaseProjectCardRecord>();
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
