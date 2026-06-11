using System.Text.Json;
using Google.Apis.Auth.OAuth2.Flows;
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
        return new GoogleMailOAuthStatusDto
        {
            IsConfigured = _options.HasGoogleOAuthClientSecrets,
            HasRefreshToken = HasRefreshTokenConfigured(),
            FromEmail = "official.phantomai@gmail.com"
        };
    }

    public bool HasRefreshTokenConfigured()
    {
        return _secrets.FindByKey(GmailRefreshTokenSecretKey) != null;
    }

    public GoogleMailOAuthStartResultDto StartAuthorization(string publicBackendBaseUrl)
    {
        if (!_options.HasGoogleOAuthClientSecrets)
        {
            throw new BackendValidationException(
                "Google OAuth client secrets are not configured.");
        }

        var redirectUri = ResolveRedirectUri(publicBackendBaseUrl);
        var flow = BuildFlow();
        var stateToken = _tokenService.GenerateOpaqueToken();
        _states.Save(new OAuthPendingStateRecord
        {
            Provider = Provider,
            StateToken = stateToken,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
            CreatedAtUtc = DateTime.UtcNow
        });

        var authorizationUrl = flow.CreateAuthorizationCodeRequest(redirectUri);
        authorizationUrl.State = stateToken;
        authorizationUrl.Scope = GmailSendScope;
        var builtUrl = authorizationUrl.Build().ToString();
        var separator = builtUrl.Contains('?') ? "&" : "?";
        var finalAuthorizationUrl =
            $"{builtUrl}{separator}access_type=offline&prompt=consent";

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
        var secret = _secrets.FindByKey(GmailRefreshTokenSecretKey)
            ?? throw new InvalidOperationException("Google Gmail refresh token is not configured.");
        return _protector.Unprotect(secret.EncryptedValue);
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
