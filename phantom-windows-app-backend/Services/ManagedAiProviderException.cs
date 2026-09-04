namespace Phantom.WindowsApp.Backend.Services;

public sealed class ManagedAiProviderException : Exception
{
    public ManagedAiProviderException(
        string code,
        bool isTransient,
        int? providerStatusCode = null,
        Exception? innerException = null)
        : base("The managed AI provider could not complete the request.", innerException)
    {
        Code = code;
        IsTransient = isTransient;
        ProviderStatusCode = providerStatusCode;
    }

    public string Code { get; }
    public bool IsTransient { get; }
    public int? ProviderStatusCode { get; }

    public static ManagedAiProviderException FromStatusCode(int statusCode) => new(
        statusCode == StatusCodes.Status429TooManyRequests ? "rate_limited" : $"provider_{statusCode / 100}xx",
        statusCode == StatusCodes.Status408RequestTimeout
            || statusCode == StatusCodes.Status429TooManyRequests
            || statusCode >= StatusCodes.Status500InternalServerError,
        statusCode);

    public static ManagedAiProviderException FromFailure(Exception? error)
    {
        if (error is ManagedAiProviderException providerError) return providerError;
        if (error is TimeoutException) return new("first_token_timeout", true, innerException: error);
        if (error is HttpRequestException) return new("provider_network_error", true, innerException: error);
        return new("provider_error", true, innerException: error);
    }
}
