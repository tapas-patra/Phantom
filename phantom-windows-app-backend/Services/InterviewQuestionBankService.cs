using System.Text.Json;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class InterviewQuestionBankService : BackgroundService
{
    private readonly InterviewQuestionBankJobRepository _jobs;
    private readonly AccountRepository _accounts;
    private readonly HostedKnowledgeBaseStructuredExtractionService _llm;
    private readonly ILogger<InterviewQuestionBankService> _logger;

    public InterviewQuestionBankService(
        InterviewQuestionBankJobRepository jobs,
        AccountRepository accounts,
        HostedKnowledgeBaseStructuredExtractionService llm,
        ILogger<InterviewQuestionBankService> logger)
    {
        _jobs = jobs;
        _accounts = accounts;
        _llm = llm;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _jobs.RequeueRunningJobs();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover interview question-bank jobs on startup.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = _jobs.TryStartNextQueued();
                if (job == null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }

                try
                {
                    await ProcessAsync(job, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _jobs.MarkFailed(job.JobId, ex.Message);
                    _logger.LogError(ex, "Interview question-bank job {JobId} failed.", job.JobId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Interview question-bank worker cycle failed.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(InterviewQuestionBankJob job, CancellationToken cancellationToken)
    {
        var account = _accounts.FindByUserId(job.UserId)
            ?? throw new InvalidOperationException("Account not found for interview question-bank job.");
        var inputs = JsonSerializer.Deserialize<string[]>(job.RawQuestionInputsJson) ?? Array.Empty<string>();
        var prompt = """
Return strict JSON only with this shape: {"questions":[""]}

Create an interview question bank from noisy speech-to-text inputs.
Rules:
- Keep only questions asked by the interviewer; remove greetings, commands, filler, answers, and transcription noise.
- Group follow-up questions with the earlier question when they refer to the same subject.
- A generic adjacent follow-up such as "What is the system design and how does the flow work?" inherits the last concrete subject unless it introduces a different named subject.
- Example: "Tell me about JAQ and its architecture" followed by "Tell me the system design and how the flow works" must become one JAQ question. A later question about a bus stop is a new question.
- Rewrite each group as one clear, self-contained question without adding facts.
- Preserve distinct questions as separate items and preserve their interview order.
- Return an empty questions array when no real interview question exists.
""";
        var json = await _llm.TryCompleteJsonAsync(
            account,
            prompt,
            JsonSerializer.Serialize(inputs),
            cancellationToken)
            ?? throw new InvalidOperationException("No managed LLM is available for question-bank processing.");
        var result = JsonSerializer.Deserialize<QuestionBankLlmResult>(ExtractJson(json), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The question-bank LLM returned invalid JSON.");
        var questions = (result.Questions ?? Array.Empty<string>())
            .Select(question => question.Trim())
            .Where(question => question.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(100)
            .ToArray();
        _jobs.MarkCompleted(job, questions);
    }

    public object Update(string userId, string sessionId, InterviewQuestionBankUpdateRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new BackendValidationException("SessionId is required.");
        }

        var interviewName = request.InterviewName?.Trim() ?? string.Empty;
        if (interviewName.Length > 120)
        {
            throw new BackendValidationException("Interview name cannot exceed 120 characters.");
        }

        if (request.Questions == null || request.Questions.Count > 100)
        {
            throw new BackendValidationException("An interview question bank can contain at most 100 questions.");
        }

        var questions = request.Questions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Select(question => question.Trim())
            .ToArray();
        if (questions.Any(question => question.Length > 2000))
        {
            throw new BackendValidationException("Each interview question cannot exceed 2,000 characters.");
        }

        var job = _jobs.UpdateCompleted(userId, sessionId.Trim(), interviewName, questions)
            ?? throw new BackendValidationException("Interview question bank not found.");
        return new
        {
            sessionId = job.SessionId,
            interviewName = job.InterviewName,
            questions,
            interviewStartedAtUtc = job.InterviewStartedAtUtc,
            interviewEndedAtUtc = job.InterviewEndedAtUtc
        };
    }

    private static string ExtractJson(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException("The question-bank LLM returned invalid JSON.");
        }

        return value[start..(end + 1)];
    }

    private sealed class QuestionBankLlmResult
    {
        public string[] Questions { get; set; } = Array.Empty<string>();
    }
}
