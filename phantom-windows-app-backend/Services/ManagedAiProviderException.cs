using Microsoft.AspNetCore.Http;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiProviderException : Exception
{
    public ManagedAiProviderException(
        string code,
        bool isTransient,
        int? providerStatusCode = null,
        int? retryAfterSeconds = null,
        Exception? innerException = null)
        : base("The managed AI provider could not complete the request.", innerException)
    {
        Code = code;
        IsTransient = isTransient;
        ProviderStatusCode = providerStatusCode;
        RetryAfterSeconds = retryAfterSeconds;
    }

    public string Code { get; }
    public bool IsTransient { get; }
    public int? ProviderStatusCode { get; }
    public int? RetryAfterSeconds { get; }

    public static ManagedAiProviderException FromStatusCode(int statusCode) => new(
        statusCode == StatusCodes.Status429TooManyRequests ? "rate_limited" : $"provider_{statusCode / 100}xx",
        statusCode == StatusCodes.Status408RequestTimeout
            || statusCode == StatusCodes.Status429TooManyRequests
            || statusCode >= StatusCodes.Status500InternalServerError,
        statusCode,
        statusCode == StatusCodes.Status429TooManyRequests
            ? 300
            : statusCode is StatusCodes.Status408RequestTimeout
                or StatusCodes.Status500InternalServerError
                or StatusCodes.Status502BadGateway
                or StatusCodes.Status503ServiceUnavailable
                or StatusCodes.Status504GatewayTimeout
                ? 30
                : null);

    public static ManagedAiProviderException FromFailure(Exception? error)
    {
        if (error is ManagedAiProviderException providerError) return providerError;
        var failure = ProviderResiliencePolicy.Classify(error ?? new InvalidOperationException("provider_error"));
        var transient = failure.Kind is ProviderFailureKind.RateLimited or ProviderFailureKind.Transient;
        int? retryAfter = failure.Kind is ProviderFailureKind.RateLimited or ProviderFailureKind.Transient
            ? Math.Max(1, (int)Math.Ceiling(failure.Cooldown.TotalSeconds))
            : null;
        return new(failure.ErrorCode, transient, retryAfterSeconds: retryAfter, innerException: error);
    }
}
