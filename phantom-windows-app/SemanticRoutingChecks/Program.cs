using System.Reflection;
using SecureOverlay.Services;

var manager = new ConversationManager(new FakeAiService("HYBRID"), "", new ModelConfig());

AssertRoute(manager, "Explain dependency injection", "Direct");
AssertRoute(manager, "Tell me about Kubernetes", "SemanticProbe");
AssertSemanticIntent(manager, "How did you use caching, and why?", "Hybrid");

Console.WriteLine("Semantic routing checks passed.");

static void AssertRoute(ConversationManager manager, string question, string expectedType)
{
    var method = typeof(ConversationManager).GetMethod("RouteResponse", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("RouteResponse was not found.");
    var plan = method.Invoke(manager, new object[] { question })
        ?? throw new InvalidOperationException("RouteResponse returned null.");
    var type = plan.GetType().GetProperty("Type")?.GetValue(plan)?.ToString();
    if (!string.Equals(type, expectedType, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected {expectedType} for '{question}', got {type}.");
    }
}

static void AssertSemanticIntent(ConversationManager manager, string question, string expectedIntent)
{
    var method = typeof(ConversationManager).GetMethod("ClassifySemanticIntentAsync", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("ClassifySemanticIntentAsync was not found.");
    var task = method.Invoke(manager, new object[] { question, CancellationToken.None }) as Task
        ?? throw new InvalidOperationException("Semantic classifier did not return a task.");
    task.GetAwaiter().GetResult();
    var intent = task.GetType().GetProperty("Result")?.GetValue(task)?.ToString();
    if (!string.Equals(intent, expectedIntent, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected {expectedIntent} semantic intent, got {intent}.");
    }
}

sealed class FakeAiService : IAIService
{
    private readonly string _response;

    public FakeAiService(string response) => _response = response;

    public bool IsConfigured() => true;

    public string GetProviderName() => "Test";

    public Task<string> SendMessageAsync(List<ConversationMessage> messages, string? imageBase64 = null)
        => Task.FromResult(_response);

    public Task<string> SendMessageStreamAsync(
        List<ConversationMessage> messages,
        Action<string> onChunkReceived,
        CancellationToken cancellationToken = default,
        string? imageBase64 = null)
    {
        onChunkReceived(_response);
        return Task.FromResult(_response);
    }
}
