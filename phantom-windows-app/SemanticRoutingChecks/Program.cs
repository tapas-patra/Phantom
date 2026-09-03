using System.Text.Json;
using SecureOverlay.Domain;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Services;

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

if (fixtures.Contracts.Count < 27 || fixtures.Contracts.Select(x => x.Id).Distinct().Count() != fixtures.Contracts.Count)
    throw new InvalidOperationException("The shared golden corpus is incomplete or has duplicate IDs.");
if (fixtures.Contracts.Where(x => x.Mode == "interview").Select(x => x.Id).Intersect(Enumerable.Range(1, 26)).Count() != 26)
    throw new InvalidOperationException("Interview fixtures 1 through 26 are required.");
if (fixtures.Contracts.Any(x => x.MaxNormalCalls is < 1 or > 2))
    throw new InvalidOperationException("A fixture permits an invalid normal model-call count.");
if (fixtures.Logging.Count < 4 || fixtures.Logging.Any(x => x.TerminalEvents != 1))
    throw new InvalidOperationException("Logging correlation fixtures are incomplete.");

var directFrame = fixtures.Parser.First(x => x.ExpectedAction == "answer").Chunks;
var directCalls = 0;
var directVisible = string.Empty;
var direct = await new LiveCopilotOrchestrator().ExecuteAsync(
    Array.Empty<string>(), Array.Empty<string>(),
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

foreach (var secret in fixtures.SensitiveSamples)
{
    var allowlist = new[] { "question_length_bucket", "provider", "model", "answer_basis" };
    if (allowlist.Any(value => value.Contains(secret, StringComparison.Ordinal)))
        throw new InvalidOperationException("Sensitive fixture leaked into the telemetry allowlist.");
}

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

sealed class FixtureRoot
{
    public string Version { get; set; } = "";
    public List<ParserFixture> Parser { get; set; } = new();
    public List<InvalidFixture> Invalid { get; set; } = new();
    public List<AnswerCompletionFixture> AnswerCompletion { get; set; } = new();
    public List<ContractFixture> Contracts { get; set; } = new();
    public List<LoggingFixture> Logging { get; set; } = new();
    public List<string> SensitiveSamples { get; set; } = new();
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
