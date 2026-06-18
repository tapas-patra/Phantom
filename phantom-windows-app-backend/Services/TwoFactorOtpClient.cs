using System.Net.Http.Headers;
using System.Text.Json;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class TwoFactorOtpClient
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly BackendOptions _options;

    public TwoFactorOtpClient(BackendOptions options)
    {
        _options = options;
    }

    public async Task<(string SessionId, DateTime ExpiresAtUtc)> SendAsync(string phoneNumberE164, CancellationToken cancellationToken)
    {
        if (!_options.HasOtpApiKey)
        {
            throw new InvalidOperationException("OTP API key is not configured.");
        }

        var phone = phoneNumberE164.TrimStart('+');
        var sendUrl = _options.OtpSendUrlTemplate
            .Replace("{apiKey}", Uri.EscapeDataString(_options.OtpApiKey), StringComparison.Ordinal)
            .Replace("{phone}", Uri.EscapeDataString(phone), StringComparison.Ordinal)
            .Replace("{template}", Uri.EscapeDataString(_options.OtpTemplateName), StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, sendUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OTP send failed: {(int)response.StatusCode}.");
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var status = root.TryGetProperty("Status", out var statusElement) ? statusElement.GetString() : "Error";
        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(root.TryGetProperty("Details", out var detailsElement)
                ? detailsElement.GetString() ?? "OTP send failed."
                : "OTP send failed.");
        }

        var sessionId = root.TryGetProperty("Details", out var sessionElement)
            ? sessionElement.GetString() ?? string.Empty
            : string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("OTP provider did not return a session ID.");
        }

        return (sessionId, DateTime.UtcNow.AddMinutes(10));
    }

    public async Task VerifyAsync(string providerSessionId, string otpCode, CancellationToken cancellationToken)
    {
        var verifyUrl = _options.OtpVerifyUrlTemplate
            .Replace("{apiKey}", Uri.EscapeDataString(_options.OtpApiKey), StringComparison.Ordinal)
            .Replace("{sessionId}", Uri.EscapeDataString(providerSessionId), StringComparison.Ordinal)
            .Replace("{otp}", Uri.EscapeDataString(otpCode), StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, verifyUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await HttpClient.SendAsync(request, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OTP verification failed: {(int)response.StatusCode}.");
        }

        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        var status = root.TryGetProperty("Status", out var statusElement) ? statusElement.GetString() : "Error";
        if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(root.TryGetProperty("Details", out var detailsElement)
                ? detailsElement.GetString() ?? "Invalid OTP."
                : "Invalid OTP.");
        }
    }
}
