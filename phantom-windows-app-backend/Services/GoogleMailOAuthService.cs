using System.Text.Json;
using Google.Apis.Auth.OAuth2.Flows;
using Microsoft.AspNetCore.WebUtilities;
using Phantom.WindowsApp.Backend.Contracts;
using Phantom.WindowsApp.Backend.Domain;
using Phantom.WindowsApp.Backend.Infrastructure;
using Phantom.WindowsApp.Backend.Persistence;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class GoogleMailOAuthService
{
    private const string GmailSendScope = "https://www.googleapis.com/auth/gmail.send";
    private const string GmailRefreshTokenSecretKey = "gmail_api_refresh_token";
    private const string Provider = "gmail_sender";

    private readonly BackendOptions _options;
    private readonly OAuthPendingStateRepository _states;
    private readonly IntegrationSecretRepository _secrets;
    private readonly SecretProtector _protector;
    private readonly TokenService _tokenService;

    public GoogleMailOAuthService(
        BackendOptions options,
        OAuthPendingStateRepository states,
        IntegrationSecretRepository secrets,
        SecretProtector protector,
        TokenService tokenService)
    {
        _options = options;
        _states = states;
        _secrets = secrets;
        _protector = protector;
        _tokenService = tokenService;
    }

    public GoogleMailOAuthStatusDto GetStatus()
    {
        var hasRefreshToken = HasRefreshTokenConfigured();
        var hasValidRefreshToken = false;
        var statusMessage = string.Empty;
        if (hasRefreshToken)
        {
            hasValidRefreshToken = TryValidateRefreshToken(out statusMessage);
        }

        return new GoogleMailOAuthStatusDto
        {
            IsConfigured = _options.HasGoogleOAuthClientSecrets,
            HasRefreshToken = hasRefreshToken,
            HasValidRefreshToken = hasValidRefreshToken,
            NeedsReconnect = hasRefreshToken && !hasValidRefreshToken,
            StatusLabel = !hasRefreshToken
                ? "Not connected"
                : hasValidRefreshToken
                    ? "Connected"
                    : "Reconnect required",
            StatusMessage = !hasRefreshToken
                ? "Gmail OAuth has not been connected yet."
                : hasValidRefreshToken
                    ? "Stored Gmail refresh token is valid."
                    : statusMessage,
            FromEmail = "official.phantomai@gmail.com"
        };
    }

    public bool HasRefreshTokenConfigured()
    {
        return _secrets.FindByKey(GmailRefreshTokenSecretKey) != null
            || _options.HasGoogleOAuthRefreshToken;
    }

    public GoogleMailOAuthStartResultDto StartAuthorization(string publicBackendBaseUrl)
    {
        if (!_options.HasGoogleOAuthClientSecrets)
        {
            throw new BackendValidationException(
                "Google OAuth client secrets are not configured.");
        }

        var redirectUri = ResolveRedirectUri(publicBackendBaseUrl);
        var stateToken = _tokenService.GenerateOpaqueToken();
        _states.Save(new OAuthPendingStateRecord
        {
            Provider = Provider,
            StateToken = stateToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            CreatedAtUtc = DateTime.UtcNow
        });

        var clientSecrets = LoadClientSecrets();
        var finalAuthorizationUrl = QueryHelpers.AddQueryString(
            "https://accounts.google.com/o/oauth2/v2/auth",
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["client_id"] = clientSecrets.ClientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = GmailSendScope,
                ["state"] = stateToken,
                ["access_type"] = "offline",
                ["prompt"] = "consent"
            });

        return new GoogleMailOAuthStartResultDto
        {
            AuthorizationUrl = finalAuthorizationUrl,
            RedirectUri = redirectUri
        };
    }

    public async Task CompleteAuthorizationAsync(string code, string stateToken, string publicBackendBaseUrl, CancellationToken cancellationToken)
    {
        var pendingState = _states.Find(Provider, stateToken);
        if (pendingState == null || pendingState.ExpiresAtUtc <= DateTime.UtcNow)
        {
            throw new BackendValidationException("The Google OAuth state is missing or expired.");
        }

        var redirectUri = ResolveRedirectUri(publicBackendBaseUrl);
        var flow = BuildFlow();
        var tokenResponse = await flow.ExchangeCodeForTokenAsync(
            userId: Provider,
            code: code,
            redirectUri: redirectUri,
            taskCancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(tokenResponse.RefreshToken))
        {
            throw new BackendValidationException(
                "Google OAuth did not return a refresh token. Re-run consent with prompt=consent and access_type=offline.");
        }

        _secrets.Save(new IntegrationSecretRecord
        {
            SecretKey = GmailRefreshTokenSecretKey,
            EncryptedValue = _protector.Protect(tokenResponse.RefreshToken),
            UpdatedAtUtc = DateTime.UtcNow
        });
        _states.Delete(stateToken);
    }

    public string GetRefreshToken()
    {
        var secret = _secrets.FindByKey(GmailRefreshTokenSecretKey);
        if (secret != null)
        {
            return _protector.Unprotect(secret.EncryptedValue);
        }

        if (_options.HasGoogleOAuthRefreshToken)
        {
            return _options.GoogleOAuthRefreshToken;
        }

        throw new InvalidOperationException("Google Gmail refresh token is not configured.");
    }

    public GoogleAuthorizationCodeFlow BuildFlow()
    {
        var clientSecrets = LoadClientSecrets();
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = clientSecrets,
            Scopes = new[] { GmailSendScope }
        });
    }

    public bool TryValidateRefreshToken(out string message)
    {
        try
        {
            var flow = BuildFlow();
            var refreshToken = GetRefreshToken();
            var token = flow.RefreshTokenAsync(
                userId: Provider,
                refreshToken: refreshToken,
                taskCancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            if (string.IsNullOrWhiteSpace(token.AccessToken))
            {
                message = "Gmail OAuth refresh completed without an access token.";
                return false;
            }

            message = "Stored Gmail refresh token is valid.";
            return true;
        }
        catch (Exception ex)
        {
            message = $"Gmail OAuth token refresh failed: {ex.Message}";
            return false;
        }
    }

    private string ResolveRedirectUri(string publicBackendBaseUrl)
    {
        if (!string.IsNullOrWhiteSpace(_options.GoogleOAuthRedirectUri))
        {
            return _options.GoogleOAuthRedirectUri.Trim();
        }

        return $"{publicBackendBaseUrl.TrimEnd('/')}/api/admin/integrations/gmail/oauth/callback";
    }

    private Google.Apis.Auth.OAuth2.ClientSecrets LoadClientSecrets()
    {
        var json = !string.IsNullOrWhiteSpace(_options.GoogleOAuthClientSecretsJson)
            ? _options.GoogleOAuthClientSecretsJson
            : File.ReadAllText(_options.GoogleOAuthClientSecretsPath);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var section = root.TryGetProperty("web", out var web)
            ? web
            : root.TryGetProperty("installed", out var installed)
                ? installed
                : throw new InvalidOperationException("Google OAuth client secrets JSON must contain 'web' or 'installed'.");

        return new Google.Apis.Auth.OAuth2.ClientSecrets
        {
            ClientId = section.GetProperty("client_id").GetString() ?? string.Empty,
            ClientSecret = section.GetProperty("client_secret").GetString() ?? string.Empty
        };
    }
}
