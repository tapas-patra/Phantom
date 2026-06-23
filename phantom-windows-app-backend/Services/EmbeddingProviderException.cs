namespace Phantom.WindowsApp.Backend.Services;

public sealed class EmbeddingProviderException : Exception
{
    public EmbeddingProviderException(
        string message,
        bool isTransient,
        int? providerStatusCode = null,
        int? retryAfterSeconds = null,
        Exception? innerException = null) : base(message, innerException)
    {
        IsTransient = isTransient;
        ProviderStatusCode = providerStatusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public bool IsTransient { get; }
    public int? ProviderStatusCode { get; }
    public int? RetryAfterSeconds { get; }
}
