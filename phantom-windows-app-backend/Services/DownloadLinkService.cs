using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class DownloadLinkService
{
    private static readonly IReadOnlyDictionary<string, string> AssetNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["windows"] = "Phantom-Windows-x64.zip",
        ["macos"] = "Phantom-macOS.zip"
    };

    private readonly BackendOptions _options;
    private readonly AccountStateService _accounts;

    public DownloadLinkService(BackendOptions options, AccountStateService accounts)
    {
        _options = options;
        _accounts = accounts;
    }

    public object CreateSignedLink(string userId, string email, string platform, string publicBackendBaseUrl)
    {
        EnsureEligible(userId, email);
        var normalizedPlatform = NormalizePlatform(platform);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.DownloadLinkTtlMinutes);
        var payload = $"{userId}|{normalizedPlatform}|{new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()}|{Guid.NewGuid():N}";
        var encodedPayload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signature = Sign(encodedPayload);
        var token = $"{encodedPayload}.{signature}";
        return new
        {
            platform = normalizedPlatform,
            expiresAtUtc,
            url = $"{publicBackendBaseUrl.TrimEnd('/')}/api/desktop/downloads/file?token={Uri.EscapeDataString(token)}"
        };
    }

    public string ResolveAssetUrl(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new BackendValidationException("Download token is required.");
        }

        var parts = token.Split('.', 2);
        if (parts.Length != 2 || !FixedTimeEquals(Sign(parts[0]), parts[1]))
        {
            throw new BackendValidationException("Download link is invalid.");
        }

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0]));
        }
        catch (FormatException)
        {
            throw new BackendValidationException("Download link is invalid.");
        }

        var fields = payload.Split('|');
        if (fields.Length != 4 || !long.TryParse(fields[2], out var expiresUnix))
        {
            throw new BackendValidationException("Download link is invalid.");
        }

        if (DateTimeOffset.FromUnixTimeSeconds(expiresUnix) <= DateTimeOffset.UtcNow)
        {
            throw new BackendValidationException("Download link has expired. Request a new link from your dashboard.");
        }

        var account = _accounts.RequireAccount(fields[0], string.Empty);
        EnsureEligible(account.UserId, account.Email);
        var platform = NormalizePlatform(fields[1]);
        return $"https://github.com/{_options.ReleaseRepository}/releases/download/{_options.ReleaseTag}/{AssetNames[platform]}";
    }

    private void EnsureEligible(string userId, string email)
    {
        var account = _accounts.RequireAccount(userId, email);
        if (!account.EmailVerified)
        {
            throw new BackendValidationException("Verify your email before downloading Phantom.");
        }

        if (account.IsManualLockActive)
        {
            throw new BackendValidationException("This account is temporarily locked.");
        }
    }

    private string Sign(string payload)
    {
        var signingKey = string.IsNullOrWhiteSpace(_options.DownloadSigningKey)
            ? _options.SecretEncryptionKey
            : _options.DownloadSigningKey;
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new BackendValidationException("Secure download links are not configured.");
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
        return WebEncoders.Base64UrlEncode(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static bool FixedTimeEquals(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    private static string NormalizePlatform(string platform)
    {
        var normalized = platform?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AssetNames.ContainsKey(normalized))
        {
            throw new BackendValidationException("Platform must be 'windows' or 'macos'.");
        }

        return normalized;
    }
}
