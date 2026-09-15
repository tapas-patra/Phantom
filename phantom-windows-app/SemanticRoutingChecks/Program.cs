using System.Text.Json;
using SecureOverlay.Domain;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Helpers;
using SecureOverlay.Services;
using BackendPolicy = Phantom.WindowsApp.Backend.Services.ProviderResiliencePolicy;

var fixtures = JsonSerializer.Deserialize<FixtureRoot>(File.ReadAllText(FindFixtures()), new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? throw new InvalidOperationException("Shared live-copilot fixtures could not be decoded.");

foreach (var fixture in fixtures.Parser)
{
    var parser = new PhantomControlFrameParser(fixture.AllowedEntityIds, fixture.AllowedDocumentIds);
    var body = string.Empty;
    foreach (var chunk in fixture.Chunks) body += parser.Feed(chunk);
    var decision = parser.Complete();
    Equal(fixture.ExpectedAction, decision.Action.ToString().ToLowerInvariant(), fixture.Name + " action");
    Equal(fixture.ExpectedBody, body, fixture.Name + " body");
    if (body.Contains(PhantomControlFrameParser.ProtocolLine, StringComparison.Ordinal))
        throw new InvalidOperationException(fixture.Name + " leaked the private control prefix.");
}

foreach (var fixture in fixtures.Invalid)
{
    var rejected = false;
    try
    {
        var parser = new PhantomControlFrameParser(new[] { "payment-migration" }, new[] { "resume-document-id" });
        _ = parser.Feed(fixture.Frame);
        _ = parser.Complete();
    }
    catch (PhantomProtocolException) { rejected = true; }
    if (!rejected) throw new InvalidOperationException(fixture.Name + " was accepted.");
}

foreach (var fixture in fixtures.AnswerCompletion)
    Equal(fixture.ExpectedComplete.ToString(), LiveCopilotOrchestrator.IsCompleteAnswer(fixture.Body).ToString(), fixture.Name + " completion");

if (fixtures.Contracts.Count < 30 || fixtures.Contracts.Select(x => x.Id).Distinct().Count() != fixtures.Contracts.Count)
    throw new InvalidOperationException("The shared golden corpus is incomplete or has duplicate IDs.");
if (fixtures.Contracts.Where(x => x.Mode == "interview").Select(x => x.Id).Intersect(Enumerable.Range(1, 26)).Count() != 26)
    throw new InvalidOperationException("Interview fixtures 1 through 26 are required.");
if (fixtures.Contracts.Any(x => x.MaxNormalCalls is < 1 or > 2))
    throw new InvalidOperationException("A fixture permits an invalid normal model-call count.");
if (fixtures.Logging.Count < 4 || fixtures.Logging.Any(x => x.TerminalEvents != 1))
    throw new InvalidOperationException("Logging correlation fixtures are incomplete.");
Equal(fixtures.Resilience.ManagedBackendMaxAttempts.ToString(), BackendPolicy.ManagedBackendMaxAttempts.ToString(), "backend max attempts");
Equal(fixtures.Resilience.ManagedDesktopMaxAttempts.ToString(), ProviderResiliencePolicy.ManagedDesktopMaxAttempts.ToString(), "managed desktop max attempts");
Equal(fixtures.Resilience.ByoDesktopMaxAttempts.ToString(), ProviderResiliencePolicy.ByoDesktopMaxAttempts.ToString(), "BYO desktop max attempts");
foreach (var fixture in fixtures.Resilience.Classification)
{
    var input = fixture.StatusCode?.ToString() ?? fixture.Message;
    var desktop = ProviderResiliencePolicy.Classify(input);
    Equal(fixture.ExpectedKind, FailureName(desktop.Kind), fixture.Name + " desktop kind");
    Equal(fixture.CooldownSeconds.ToString(), ((int)desktop.Cooldown.TotalSeconds).ToString(), fixture.Name + " desktop cooldown");
    var backend = BackendPolicy.Classify(BackendFixtureError(fixture));
    Equal(fixture.ExpectedKind, BackendFailureName(backend.Kind), fixture.Name + " backend kind");
    Equal(fixture.CooldownSeconds.ToString(), ((int)backend.Cooldown.TotalSeconds).ToString(), fixture.Name + " backend cooldown");
}
foreach (var fixture in fixtures.Resilience.Retry)
{
    var failure = Failure(fixture.FailureKind);
    var actual = fixture.Lane switch
    {
        "managed_backend" => BackendPolicy.CanRetry(BackendFailure(fixture.FailureKind), fixture.Attempt, fixture.HasOutput),
        "managed_desktop" => ProviderResiliencePolicy.CanRetry(failure, fixture.Attempt, ProviderResiliencePolicy.ManagedDesktopMaxAttempts, fixture.HasOutput),
        _ => ProviderResiliencePolicy.CanRetry(failure, fixture.Attempt, ProviderResiliencePolicy.ByoDesktopMaxAttempts, fixture.HasOutput)
    };
    Equal(fixture.Expected.ToString(), actual.ToString(), fixture.Name);
}
foreach (var fixture in fixtures.Resilience.LaneTransitions)
    Equal(fixture.Expected.ToString(), ProviderResiliencePolicy.CanCrossLane(fixture.From, fixture.To, fixture.OptedIn, fixture.HasOutput).ToString(), fixture.Name);
var firstCallPrompt = CopilotPromptRegistry.BuildFirstCallPrompt(
    CopilotMode.Interview, InterviewDeliveryStyle.Standard, null, string.Empty, string.Empty,
    Array.Empty<RetrievedContextSnippet>());
if (fixtures.PromptRequirements.Any(requirement => !firstCallPrompt.Contains(requirement, StringComparison.Ordinal)))
    throw new InvalidOperationException("The Windows prompt is missing a shared grounding requirement.");
if (fixtures.DeliveryStyleRequirements.Standard.Any(requirement => !firstCallPrompt.Contains(requirement, StringComparison.Ordinal)))
    throw new InvalidOperationException("The Windows Standard prompt is missing a shared delivery-style requirement.");
var desiPrompt = CopilotPromptRegistry.BuildFirstCallPrompt(
    CopilotMode.Interview, InterviewDeliveryStyle.Desi, null, string.Empty, string.Empty,
    Array.Empty<RetrievedContextSnippet>());
if (fixtures.DeliveryStyleRequirements.Desi.Any(requirement => !desiPrompt.Contains(requirement, StringComparison.Ordinal)))
    throw new InvalidOperationException("The Windows Desi prompt is missing a shared delivery-style requirement.");
if (string.Equals(firstCallPrompt, desiPrompt, StringComparison.Ordinal) ||
    firstCallPrompt.Contains("Delivery style is Desi", StringComparison.Ordinal) ||
    desiPrompt.Contains("Delivery style is Standard", StringComparison.Ordinal))
    throw new InvalidOperationException("The Windows delivery-style prompts are not isolated.");
var repairPrompt = CopilotPromptRegistry.BuildFirstCallPrompt(
    CopilotMode.Interview, InterviewDeliveryStyle.Standard, null, string.Empty, string.Empty,
    Array.Empty<RetrievedContextSnippet>(), protocolRepair: true);
if (!repairPrompt.EndsWith(fixtures.RepairPromptSuffix, StringComparison.Ordinal))
    throw new InvalidOperationException("The Windows protocol-repair instruction is not the final prompt authority.");

var directFrame = fixtures.Parser.First(x => x.ExpectedAction == "answer").Chunks;
var directCalls = 0;
var directVisible = string.Empty;
var direct = await new LiveCopilotOrchestrator().ExecuteAsync(
    Array.Empty<string>(), Array.Empty<string>(),
    "What is optimistic locking?", false, false,
    (publish, _, _) =>
    {
        directCalls++;
        foreach (var chunk in directFrame) publish(chunk);
        return Task.FromResult((string.Concat(directFrame), string.Empty));
    },
    (_, _) => throw new InvalidOperationException("Direct fixture retrieved."),
    (_, _) => throw new InvalidOperationException("Direct fixture used a second call."),
    chunk => directVisible += chunk, null, CancellationToken.None);
Equal("1", directCalls.ToString(), "direct model calls");
Equal(direct.Answer, directVisible, "direct visible body");

var retrieveFixture = fixtures.Parser.First(x => x.ExpectedAction == "retrieve");
var retrieveCalls = 0;
var finalVisible = string.Empty;
var retrieved = await new LiveCopilotOrchestrator().ExecuteAsync(
    retrieveFixture.AllowedEntityIds, retrieveFixture.AllowedDocumentIds,
    "Walk me through the payment migration conflict.", false, false,
    (publish, _, _) =>
    {
        retrieveCalls++;
        var raw = string.Concat(retrieveFixture.Chunks);
        publish(raw);
        return Task.FromResult((raw, string.Empty));
    },
    (_, _) => Task.FromResult(new LiveCopilotRetrieval("found", new[]
    {
        new RetrievedContextSnippet { DocumentId = "resume-document-id", Text = "verified evidence" }
    }, "1")),
    (_, _) => (publish, _, _) =>
    {
        retrieveCalls++;
        publish("Grounded final answer.");
        return Task.FromResult(("Grounded final answer.", string.Empty));
    },
    chunk => finalVisible += chunk, null, CancellationToken.None);
Equal("2", retrieveCalls.ToString(), "retrieve model calls");
Equal("Grounded final answer.", retrieved.Answer, "retrieve final answer");
Equal(retrieved.Answer, finalVisible, "retrieve visible body");

var fallbackCalls = 0;
var fallbackVisible = string.Empty;
const string fallbackRaw = "Optimistic locking detects a conflicting write without a control header.";
var fallback = await new LiveCopilotOrchestrator().ExecuteAsync(
    Array.Empty<string>(), Array.Empty<string>(),
    "What is optimistic locking?", false, false,
    (publish, _, _) =>
    {
        fallbackCalls++;
        publish(fallbackRaw);
        return Task.FromResult((fallbackRaw, string.Empty));
    },
    (_, _) => throw new InvalidOperationException("Fallback fixture retrieved."),
    (_, _) => throw new InvalidOperationException("Fallback fixture used a second call."),
    chunk => fallbackVisible += chunk, null, CancellationToken.None);
Equal("1", fallbackCalls.ToString(), "fallback model calls");
Equal(fallbackRaw, fallback.Answer, "fallback answer");
Equal(fallback.Answer, fallbackVisible, "fallback visible body");

var repairCalls = 0;
var repairVisible = string.Empty;
var repairRejected = new List<string>();
const string headerOnly = "PHANTOM_CONTROL_V1\n";
var repaired = await new LiveCopilotOrchestrator().ExecuteAsync(
    Array.Empty<string>(), Array.Empty<string>(),
    "What is optimistic locking?", false, false,
    (publish, _, _) =>
    {
        repairCalls++;
        var raw = repairCalls == 1 ? headerOnly : fallbackRaw;
        publish(raw);
        return Task.FromResult((raw, string.Empty));
    },
    (_, _) => throw new InvalidOperationException("Repair fixture retrieved."),
    (_, _) => throw new InvalidOperationException("Repair fixture used a second call."),
    chunk => repairVisible += chunk, null, CancellationToken.None,
    protocolRejected: code => repairRejected.Add(code));
Equal("2", repairCalls.ToString(), "header-only repair model calls");
Equal(fallbackRaw, repaired.Answer, "header-only repair answer");
Equal(repaired.Answer, repairVisible, "header-only repair visible body");
if (!repairRejected.Contains("control_frame_incomplete"))
    throw new InvalidOperationException("Header-only repair did not reject the first malformed header.");

var forcePersonalFrame =
    "PHANTOM_CONTROL_V1\n{\"action\":\"answer\",\"questionType\":\"technical\",\"intent\":\"candidate_specific\",\"answerBasis\":\"profile_synthesis\",\"entityType\":\"project\",\"entityId\":\"payment-migration\",\"retrievalQuery\":\"\",\"preferredDocumentIds\":[],\"targetSeconds\":40,\"allowCode\":false,\"confidence\":0.9}\nPHANTOM_BODY\nCatalog-only architecture answer.";
var forceCalls = 0;
var forceForced = false;
var forceVisible = string.Empty;
var kept = await new LiveCopilotOrchestrator().ExecuteAsync(
    new[] { "payment-migration" }, new[] { "resume-document-id" },
    "Draw the architecture of Spashta.", true, false,
    (publish, _, _) =>
    {
        forceCalls++;
        publish(forcePersonalFrame);
        return Task.FromResult((forcePersonalFrame, string.Empty));
    },
    (_, _) => throw new InvalidOperationException("Personal catalog answers must not retrieve."),
    (_, _) => throw new InvalidOperationException("Personal catalog answers must not use a second call."),
    chunk => forceVisible += chunk,
    () => throw new InvalidOperationException("Personal catalog answers must not reset the first-call body."),
    CancellationToken.None,
    preferredDocumentsForDecision: _ => new[] { "resume-document-id" },
    decisionParsed: (_, _, retrieveForced) => forceForced = retrieveForced);
Equal("1", forceCalls.ToString(), "personal catalog answer model calls");
Equal("false", forceForced.ToString().ToLowerInvariant(), "retrieve_forced");
Equal("Catalog-only architecture answer.", kept.Answer, "kept first-call answer");
Equal(kept.Answer, forceVisible, "kept visible body");

var skipGeneral = LiveCopilotRetrievePolicy.ShouldForceRetrieve(
    new LiveTurnDecision(LiveCopilotAction.Answer, "technical", "general", "universal_knowledge", "none", "", "", Array.Empty<string>(), 30, false, 0.9),
    retrievalAvailable: true, hasActiveEvidence: false);
if (skipGeneral) throw new InvalidOperationException("General technical turns must not force retrieve.");

var skipActiveEvidence = LiveCopilotRetrievePolicy.ShouldForceRetrieve(
    new LiveTurnDecision(LiveCopilotAction.Answer, "personal_factual", "candidate_specific", "profile_synthesis", "project", "payment-migration", "", Array.Empty<string>(), 30, false, 0.9),
    retrievalAvailable: true, hasActiveEvidence: true);
if (skipActiveEvidence) throw new InvalidOperationException("Active evidence must suppress forced retrieve.");

var forcePersonal = LiveCopilotRetrievePolicy.ShouldForceRetrieve(
    new LiveTurnDecision(LiveCopilotAction.Answer, "technical", "candidate_specific", "profile_synthesis", "project", "payment-migration", "", Array.Empty<string>(), 40, false, 0.9),
    retrievalAvailable: true, hasActiveEvidence: false);
if (forcePersonal) throw new InvalidOperationException("Candidate-specific project turns must keep a complete first-call answer.");

foreach (var secret in fixtures.SensitiveSamples)
{
    var allowlist = new[] { "question_length_bucket", "provider", "model", "answer_basis" };
    if (allowlist.Any(value => value.Contains(secret, StringComparison.Ordinal)))
        throw new InvalidOperationException("Sensitive fixture leaked into the telemetry allowlist.");
}

var kubernetesClarify = "Just to make sure I answer the right thing — did you mean installing Kubernetes on your own machine (like minikube, kind, or kubeadm on bare VMs), or were you asking about something else, like a specific cloud or on-prem setup?";
var kubernetesOptions = ClarificationOptionParser.Parse(kubernetesClarify);
if (kubernetesOptions.Options.Count != 2)
    throw new InvalidOperationException("Kubernetes clarification did not produce two clickable options.");
if (!kubernetesOptions.Options[0].Question.Contains("own machine", StringComparison.OrdinalIgnoreCase)
    || !kubernetesOptions.Options[1].Question.Contains("cloud", StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Kubernetes clarification options were parsed incorrectly.");

var listedClarify = "Which environment should I answer for?\nOptions:\n- I meant a local minikube cluster\n- I meant GKE in the cloud";
var listedOptions = ClarificationOptionParser.Parse(listedClarify);
Equal("Which environment should I answer for?", listedOptions.DisplayText, "listed clarification display");
Equal("2", listedOptions.Options.Count.ToString(), "listed clarification count");
Equal("I meant a local minikube cluster", listedOptions.Options[0].Question, "listed clarification first option");

Console.WriteLine($"Shared live-copilot fixture suite passed ({fixtures.Version}).");

static string FindFixtures()
{
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        var current = new DirectoryInfo(start);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "shared", "live-copilot", "fixtures.json");
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
    }
    throw new FileNotFoundException("shared/live-copilot/fixtures.json was not found.");
}

static void Equal(string expected, string actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'.");
}

static string FailureName(ProviderFailureKind kind) => kind switch
{
    ProviderFailureKind.RateLimited => "rate_limited",
    ProviderFailureKind.Authentication => "authentication_failed",
    ProviderFailureKind.Transient => "provider_transient",
    ProviderFailureKind.Cancelled => "cancelled",
    _ => "provider_error"
};

static string BackendFailureName(Phantom.WindowsApp.Backend.Services.ProviderFailureKind kind) => kind switch
{
    Phantom.WindowsApp.Backend.Services.ProviderFailureKind.RateLimited => "rate_limited",
    Phantom.WindowsApp.Backend.Services.ProviderFailureKind.Authentication => "authentication_failed",
    Phantom.WindowsApp.Backend.Services.ProviderFailureKind.Transient => "provider_transient",
    Phantom.WindowsApp.Backend.Services.ProviderFailureKind.Cancelled => "cancelled",
    _ => "provider_error"
};

static ProviderFailureDecision Failure(string kind) => kind switch
{
    "rate_limited" => ProviderResiliencePolicy.Classify("429"),
    "authentication_failed" => ProviderResiliencePolicy.Classify("401"),
    "provider_transient" => ProviderResiliencePolicy.Classify("503"),
    "cancelled" => ProviderResiliencePolicy.Classify("cancelled"),
    _ => ProviderResiliencePolicy.Classify("400")
};

static Phantom.WindowsApp.Backend.Services.ProviderFailureDecision BackendFailure(string kind) => kind switch
{
    "rate_limited" => BackendPolicy.FromStatus(429),
    "authentication_failed" => BackendPolicy.FromStatus(401),
    "provider_transient" => BackendPolicy.FromStatus(503),
    "cancelled" => BackendPolicy.FromStatus(null, "cancelled"),
    _ => BackendPolicy.FromStatus(400)
};

static Exception BackendFixtureError(FailureFixture fixture)
{
    if (fixture.StatusCode.HasValue)
        return new Phantom.WindowsApp.Backend.Services.ManagedAiProviderException(
            $"provider_{fixture.StatusCode / 100}xx",
            fixture.StatusCode == 429 || fixture.StatusCode >= 500,
            providerStatusCode: fixture.StatusCode);
    if (fixture.Name == "network-timeout") return new HttpRequestException(fixture.Message);
    if (fixture.Name == "cancelled") return new OperationCanceledException(fixture.Message);
    return new InvalidOperationException(fixture.Message);
}

sealed class FixtureRoot
{
    public string Version { get; set; } = "";
    public List<ParserFixture> Parser { get; set; } = new();
    public List<InvalidFixture> Invalid { get; set; } = new();
    public List<AnswerCompletionFixture> AnswerCompletion { get; set; } = new();
    public List<ContractFixture> Contracts { get; set; } = new();
    public List<LoggingFixture> Logging { get; set; } = new();
    public ResilienceFixture Resilience { get; set; } = new();
    public List<string> PromptRequirements { get; set; } = new();
    public DeliveryStyleRequirements DeliveryStyleRequirements { get; set; } = new();
    public string RepairPromptSuffix { get; set; } = "";
    public List<string> SensitiveSamples { get; set; } = new();
}
sealed class DeliveryStyleRequirements
{
    public List<string> Standard { get; set; } = new();
    public List<string> Desi { get; set; } = new();
}
sealed class ParserFixture
{
    public string Name { get; set; } = "";
    public List<string> AllowedEntityIds { get; set; } = new();
    public List<string> AllowedDocumentIds { get; set; } = new();
    public List<string> Chunks { get; set; } = new();
    public string ExpectedAction { get; set; } = "";
    public string ExpectedBody { get; set; } = "";
}
sealed class InvalidFixture { public string Name { get; set; } = ""; public string Frame { get; set; } = ""; }
sealed class AnswerCompletionFixture { public string Name { get; set; } = ""; public string Body { get; set; } = ""; public bool ExpectedComplete { get; set; } }
sealed class ContractFixture { public int Id { get; set; } public string Mode { get; set; } = ""; public int MaxNormalCalls { get; set; } }
sealed class LoggingFixture { public string Name { get; set; } = ""; public int TerminalEvents { get; set; } }
sealed class ResilienceFixture
{
    public int ManagedBackendMaxAttempts { get; set; }
    public int ManagedDesktopMaxAttempts { get; set; }
    public int ByoDesktopMaxAttempts { get; set; }
    public List<FailureFixture> Classification { get; set; } = new();
    public List<RetryFixture> Retry { get; set; } = new();
    public List<LaneFixture> LaneTransitions { get; set; } = new();
}
sealed class FailureFixture { public string Name { get; set; } = ""; public int? StatusCode { get; set; } public string Message { get; set; } = ""; public string ExpectedKind { get; set; } = ""; public int CooldownSeconds { get; set; } }
sealed class RetryFixture { public string Name { get; set; } = ""; public string Lane { get; set; } = ""; public string FailureKind { get; set; } = ""; public int Attempt { get; set; } public bool HasOutput { get; set; } public bool Expected { get; set; } }
sealed class LaneFixture { public string Name { get; set; } = ""; public string From { get; set; } = ""; public string To { get; set; } = ""; public bool OptedIn { get; set; } public bool HasOutput { get; set; } public bool Expected { get; set; } }
