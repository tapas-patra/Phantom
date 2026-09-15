using Microsoft.Extensions.Configuration;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BackendOptions
{
    public const string DefaultPublicWebsiteBaseUrl = "https://phantom-interview.vercel.app";

    public string DatabaseUrl { get; init; } = string.Empty;
    public string DashboardProjectionDatabaseUrl { get; init; } = string.Empty;
    public int DefaultLeaseHours { get; init; } = 24;
    public int LockTtlMinutes { get; init; } = 5;
    public decimal DefaultProCredits { get; init; } = 1.0m;
    public decimal DefaultPremiumCredits { get; init; } = 0m;
    public int SessionTtlHours { get; init; } = 12;
    public int MagicLinkTtlMinutes { get; init; } = 15;
    public int PasswordIterationCount { get; init; } = 120000;
    public int LoginAttemptWindowMinutes { get; init; } = 15;
    public int MaxFailedLoginAttempts { get; init; } = 5;
    public int EmailVerificationTtlHours { get; init; } = 24;
    public int AdminSessionTtlHours { get; init; } = 8;
    public int AdminOtpTtlMinutes { get; init; } = 10;
    public int AdminOtpMaxAttempts { get; init; } = 5;
    public int AdminPasswordResetTtlMinutes { get; init; } = 30;
    public int UserPasswordResetTtlMinutes { get; init; } = 30;
    public string AdminApiKey { get; init; } = string.Empty;
    public string InternalApiKey { get; init; } = string.Empty;
    public string SharedCookieDomain { get; init; } = string.Empty;
    public string BootstrapAdminEmail { get; init; } = string.Empty;
    public string BootstrapAdminPassword { get; init; } = string.Empty;
    public string BootstrapAdminDisplayName { get; init; } = string.Empty;
    public string PublicWebsiteBaseUrl { get; init; } = string.Empty;
    public string PublicApiBaseUrl { get; init; } = string.Empty;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public string SmtpFromEmail { get; init; } = string.Empty;
    public string SmtpFromName { get; init; } = "Phantom";
    public bool SmtpEnableSsl { get; init; } = true;
    public string GoogleOAuthClientSecretsPath { get; init; } = string.Empty;
    public string GoogleOAuthClientSecretsJson { get; init; } = string.Empty;
    public string GoogleOAuthRefreshToken { get; init; } = string.Empty;
    public string GoogleOAuthRedirectUri { get; init; } = string.Empty;
    public string SecretEncryptionKey { get; init; } = string.Empty;
    public string DownloadSigningKey { get; init; } = string.Empty;
    public int DownloadLinkTtlMinutes { get; init; } = 3;
    public string ReleaseRepository { get; init; } = "tapas-patra/phantom-release-repo";
    public string ReleaseTag { get; init; } = "desktop-latest";
    public string RazorpayKeyId { get; init; } = string.Empty;
    public string RazorpayKeySecret { get; init; } = string.Empty;
    public string RazorpayWebhookSecret { get; init; } = string.Empty;
    public string OtpProviderName { get; init; } = "2factor";
    public string OtpApiKey { get; init; } = string.Empty;
    public string OtpSenderId { get; init; } = string.Empty;
    public string OtpTemplateName { get; init; } = string.Empty;
    public string OtpSendUrlTemplate { get; init; } = string.Empty;
    public string OtpVerifyUrlTemplate { get; init; } = string.Empty;
    public string MockOtpCode { get; init; } = "111111";
    public bool KnowledgeBaseEmbeddingEnabled { get; init; } = true;
    public string KnowledgeBaseEmbeddingProvider { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultProvider;
    public string KnowledgeBaseEmbeddingBaseUrl { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultBaseUrl;
    public string KnowledgeBaseEmbeddingApiKey { get; init; } = string.Empty;
    public string KnowledgeBaseEmbeddingModel { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultModel;
    public int KnowledgeBaseEmbeddingDimensions { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions;
    public int KnowledgeBaseEmbeddingVersion { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultVersion;
    public int KnowledgeBaseEmbeddingBatchSize { get; init; } = HostedKnowledgeBaseEmbeddingDefaults.DefaultBatchSize;
    public int KnowledgeBaseQueryEmbeddingTimeoutMs { get; init; } = 5000;
    public int KnowledgeBaseQueryEmbeddingRetries { get; init; } = 0;
    public int KnowledgeBaseQueryEmbeddingRetryDelayMs { get; init; } = 75;
    public int KnowledgeBaseQueryEmbeddingCacheEntries { get; init; } = 512;
    public int KnowledgeBaseQueryEmbeddingCacheTtlMinutes { get; init; } = 30;
    public bool AllowImplicitLocalAdminBootstrap { get; init; }
    public bool AllowSeedTestUsers { get; init; }
    public bool TrustForwardedHeaders { get; init; }

    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);
    public bool HasInternalApiKey => !string.IsNullOrWhiteSpace(InternalApiKey);
    public bool IsSmtpConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost)
        && !string.IsNullOrWhiteSpace(SmtpFromEmail);
    public bool HasGoogleOAuthClientSecrets =>
        !string.IsNullOrWhiteSpace(GoogleOAuthClientSecretsPath)
        || !string.IsNullOrWhiteSpace(GoogleOAuthClientSecretsJson);
    public bool HasGoogleOAuthRefreshToken => !string.IsNullOrWhiteSpace(GoogleOAuthRefreshToken);
    public bool HasSecretEncryptionKey => !string.IsNullOrWhiteSpace(SecretEncryptionKey);
    public bool HasRazorpayCredentials =>
        !string.IsNullOrWhiteSpace(RazorpayKeyId)
        && !string.IsNullOrWhiteSpace(RazorpayKeySecret);
    public bool HasRazorpayWebhookSecret => !string.IsNullOrWhiteSpace(RazorpayWebhookSecret);
    public bool HasOtpApiKey => !string.IsNullOrWhiteSpace(OtpApiKey);
    public bool HasDashboardProjectionReplica =>
        !string.IsNullOrWhiteSpace(DashboardProjectionDatabaseUrl)
        && !string.Equals(
            DashboardProjectionDatabaseUrl.Trim(),
            DatabaseUrl.Trim(),
            StringComparison.OrdinalIgnoreCase);

    public void ValidateForProduction()
    {
        var errors = new List<string>();
        Require(errors, DatabaseUrl, nameof(DatabaseUrl));
        RequireSecret(errors, InternalApiKey, nameof(InternalApiKey));
        RequireSecret(errors, SecretEncryptionKey, nameof(SecretEncryptionKey));
        RequireSecret(errors, DownloadSigningKey, nameof(DownloadSigningKey));
        Require(errors, ReleaseRepository, nameof(ReleaseRepository));
        Require(errors, ReleaseTag, nameof(ReleaseTag));
        Require(errors, RazorpayKeyId, nameof(RazorpayKeyId));
        // Razorpay issues this opaque credential, so validate presence without
        // imposing a locally chosen length or changing the provider value.
        Require(errors, RazorpayKeySecret, nameof(RazorpayKeySecret));
        RequireSecret(errors, RazorpayWebhookSecret, nameof(RazorpayWebhookSecret));

        if (!Uri.TryCreate(PublicWebsiteBaseUrl, UriKind.Absolute, out var publicWebsite)
            || !string.Equals(publicWebsite.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{nameof(PublicWebsiteBaseUrl)} must be an absolute HTTPS URL.");
        }

        if (!IsSmtpConfigured && !(HasGoogleOAuthClientSecrets && HasSecretEncryptionKey))
        {
            errors.Add("Configure SMTP or Gmail OAuth for email verification, password reset, and admin OTP delivery.");
        }

        if (string.Equals(DownloadSigningKey, SecretEncryptionKey, StringComparison.Ordinal))
        {
            errors.Add($"{nameof(DownloadSigningKey)} must be independent from {nameof(SecretEncryptionKey)}.");
        }

        if (ReleaseRepository.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2
            || ReleaseRepository.Any(char.IsWhiteSpace))
        {
            errors.Add($"{nameof(ReleaseRepository)} must use the GitHub 'owner/repository' format.");
        }

        if (DownloadLinkTtlMinutes is < 1 or > 15) errors.Add($"{nameof(DownloadLinkTtlMinutes)} must be between 1 and 15 minutes.");
        if (AdminOtpTtlMinutes is < 2 or > 15) errors.Add($"{nameof(AdminOtpTtlMinutes)} must be between 2 and 15 minutes.");
        if (AdminOtpMaxAttempts is < 3 or > 10) errors.Add($"{nameof(AdminOtpMaxAttempts)} must be between 3 and 10.");
        if (PasswordIterationCount < 120000) errors.Add($"{nameof(PasswordIterationCount)} must be at least 120000.");
        if (AllowImplicitLocalAdminBootstrap) errors.Add($"{nameof(AllowImplicitLocalAdminBootstrap)} must be false in production.");
        if (AllowSeedTestUsers) errors.Add($"{nameof(AllowSeedTestUsers)} must be false in production.");
        if (!TrustForwardedHeaders) errors.Add($"{nameof(TrustForwardedHeaders)} must be true when production runs behind the Render reverse proxy.");
        if (string.Equals(OtpProviderName, "mock", StringComparison.OrdinalIgnoreCase)) errors.Add($"{nameof(OtpProviderName)} cannot be 'mock' in production.");

        if (errors.Count > 0)
        {
            throw new InvalidOperationException($"Unsafe production configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
        }
    }

    public static BackendOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("PhantomBackend");
        return new BackendOptions
        {
            DatabaseUrl = ReadString(
                "PHANTOM_WINDOWS_BACKEND_DATABASE_URL",
                section["DatabaseUrl"],
                string.Empty),
            DashboardProjectionDatabaseUrl = ReadString(
                "PHANTOM_DASHBOARD_BACKEND_DATABASE_URL",
                section["DashboardProjectionDatabaseUrl"],
                string.Empty),
            DefaultLeaseHours = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_LEASE_HOURS"),
                section["DefaultLeaseHours"],
                24),
            LockTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_LOCK_TTL_MINUTES"),
                section["LockTtlMinutes"],
                5),
            DefaultProCredits = ParseDecimal(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DEFAULT_PRO_CREDITS"),
                section["DefaultProCredits"],
                1.0m),
            DefaultPremiumCredits = ParseDecimal(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DEFAULT_PREMIUM_CREDITS"),
                section["DefaultPremiumCredits"],
                0m),
            SessionTtlHours = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_SESSION_TTL_HOURS"),
                section["SessionTtlHours"],
                12),
            MagicLinkTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_MAGIC_LINK_TTL_MINUTES"),
                section["MagicLinkTtlMinutes"],
                15),
            PasswordIterationCount = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_PASSWORD_ITERATIONS"),
                section["PasswordIterationCount"],
                120000),
            LoginAttemptWindowMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_LOGIN_ATTEMPT_WINDOW_MINUTES"),
                section["LoginAttemptWindowMinutes"],
                15),
            MaxFailedLoginAttempts = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_MAX_FAILED_LOGIN_ATTEMPTS"),
                section["MaxFailedLoginAttempts"],
                5),
            EmailVerificationTtlHours = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_EMAIL_VERIFICATION_TTL_HOURS"),
                section["EmailVerificationTtlHours"],
                24),
            AdminSessionTtlHours = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ADMIN_SESSION_TTL_HOURS"),
                section["AdminSessionTtlHours"],
                8),
            AdminOtpTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ADMIN_OTP_TTL_MINUTES"),
                section["AdminOtpTtlMinutes"],
                10),
            AdminOtpMaxAttempts = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ADMIN_OTP_MAX_ATTEMPTS"),
                section["AdminOtpMaxAttempts"],
                5),
            AdminPasswordResetTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ADMIN_PASSWORD_RESET_TTL_MINUTES"),
                section["AdminPasswordResetTtlMinutes"],
                30),
            UserPasswordResetTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_USER_PASSWORD_RESET_TTL_MINUTES"),
                section["UserPasswordResetTtlMinutes"],
                30),
            AdminApiKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY",
                section["AdminApiKey"],
                string.Empty),
            InternalApiKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_INTERNAL_API_KEY",
                section["InternalApiKey"],
                string.Empty),
            SharedCookieDomain = ReadString(
                "PHANTOM_SHARED_COOKIE_DOMAIN",
                section["SharedCookieDomain"],
                string.Empty),
            BootstrapAdminEmail = ReadString(
                "PHANTOM_BOOTSTRAP_ADMIN_EMAIL",
                section["BootstrapAdminEmail"],
                string.Empty),
            BootstrapAdminPassword = ReadString(
                "PHANTOM_BOOTSTRAP_ADMIN_PASSWORD",
                section["BootstrapAdminPassword"],
                string.Empty),
            BootstrapAdminDisplayName = ReadString(
                "PHANTOM_BOOTSTRAP_ADMIN_DISPLAY_NAME",
                section["BootstrapAdminDisplayName"],
                string.Empty),
            PublicWebsiteBaseUrl = ReadString(
                "PHANTOM_PUBLIC_WEBSITE_BASE_URL",
                section["PublicWebsiteBaseUrl"],
                DefaultPublicWebsiteBaseUrl),
            PublicApiBaseUrl = ReadString(
                "PHANTOM_WINDOWS_BACKEND_PUBLIC_API_BASE_URL",
                section["PublicApiBaseUrl"],
                string.Empty),
            SmtpHost = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SMTP_HOST",
                section["SmtpHost"],
                string.Empty),
            SmtpPort = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_SMTP_PORT"),
                section["SmtpPort"],
                587),
            SmtpUsername = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SMTP_USERNAME",
                section["SmtpUsername"],
                string.Empty),
            SmtpPassword = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SMTP_PASSWORD",
                section["SmtpPassword"],
                string.Empty),
            SmtpFromEmail = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SMTP_FROM_EMAIL",
                section["SmtpFromEmail"],
                string.Empty),
            SmtpFromName = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SMTP_FROM_NAME",
                section["SmtpFromName"],
                "Phantom"),
            SmtpEnableSsl = ParseBool(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_SMTP_ENABLE_SSL"),
                section["SmtpEnableSsl"],
                true),
            GoogleOAuthClientSecretsPath = ReadString(
                "PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_CLIENT_SECRETS_PATH",
                section["GoogleOAuthClientSecretsPath"],
                string.Empty),
            GoogleOAuthClientSecretsJson = ReadString(
                "PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_CLIENT_SECRETS_JSON",
                section["GoogleOAuthClientSecretsJson"],
                string.Empty),
            GoogleOAuthRefreshToken = ReadString(
                "PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REFRESH_TOKEN",
                section["GoogleOAuthRefreshToken"],
                string.Empty),
            GoogleOAuthRedirectUri = ReadString(
                "PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REDIRECT_URI",
                section["GoogleOAuthRedirectUri"],
                string.Empty),
            SecretEncryptionKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY",
                section["SecretEncryptionKey"],
                string.Empty),
            DownloadSigningKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_DOWNLOAD_SIGNING_KEY",
                section["DownloadSigningKey"],
                string.Empty),
            DownloadLinkTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_DOWNLOAD_LINK_TTL_MINUTES"),
                section["DownloadLinkTtlMinutes"],
                3),
            ReleaseRepository = ReadString(
                "PHANTOM_WINDOWS_BACKEND_RELEASE_REPOSITORY",
                section["ReleaseRepository"],
                "tapas-patra/phantom-release-repo"),
            ReleaseTag = ReadString(
                "PHANTOM_WINDOWS_BACKEND_RELEASE_TAG",
                section["ReleaseTag"],
                "desktop-latest"),
            RazorpayKeyId = ReadString(
                "PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_ID",
                section["RazorpayKeyId"],
                string.Empty),
            RazorpayKeySecret = ReadString(
                "PHANTOM_WINDOWS_BACKEND_RAZORPAY_KEY_SECRET",
                section["RazorpayKeySecret"],
                string.Empty),
            RazorpayWebhookSecret = ReadString(
                "PHANTOM_WINDOWS_BACKEND_RAZORPAY_WEBHOOK_SECRET",
                section["RazorpayWebhookSecret"],
                string.Empty),
            OtpProviderName = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_PROVIDER",
                section["OtpProviderName"],
                "2factor"),
            OtpApiKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_API_KEY",
                section["OtpApiKey"],
                string.Empty),
            OtpSenderId = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_SENDER_ID",
                section["OtpSenderId"],
                string.Empty),
            OtpTemplateName = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_TEMPLATE_NAME",
                section["OtpTemplateName"],
                string.Empty),
            OtpSendUrlTemplate = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_SEND_URL_TEMPLATE",
                section["OtpSendUrlTemplate"],
                "https://2factor.in/API/V1/{apiKey}/SMS/{phone}/AUTOGEN/{template}"),
            OtpVerifyUrlTemplate = ReadString(
                "PHANTOM_WINDOWS_BACKEND_OTP_VERIFY_URL_TEMPLATE",
                section["OtpVerifyUrlTemplate"],
                "https://2factor.in/API/V1/{apiKey}/SMS/VERIFY3/{sessionId}/{otp}"),
            MockOtpCode = ReadString(
                "PHANTOM_WINDOWS_BACKEND_MOCK_OTP_CODE",
                section["MockOtpCode"],
                "111111"),
            KnowledgeBaseEmbeddingEnabled = ParseBool(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_ENABLED"),
                section["KnowledgeBaseEmbeddingEnabled"],
                true),
            KnowledgeBaseEmbeddingProvider = ReadString(
                "PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_PROVIDER",
                section["KnowledgeBaseEmbeddingProvider"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultProvider),
            KnowledgeBaseEmbeddingBaseUrl = ReadString(
                "PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BASE_URL",
                section["KnowledgeBaseEmbeddingBaseUrl"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultBaseUrl),
            KnowledgeBaseEmbeddingApiKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_API_KEY",
                section["KnowledgeBaseEmbeddingApiKey"],
                string.Empty),
            KnowledgeBaseEmbeddingModel = ReadString(
                "PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_MODEL",
                section["KnowledgeBaseEmbeddingModel"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultModel),
            KnowledgeBaseEmbeddingDimensions = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_DIMENSIONS"),
                section["KnowledgeBaseEmbeddingDimensions"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultDimensions),
            KnowledgeBaseEmbeddingVersion = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_VERSION"),
                section["KnowledgeBaseEmbeddingVersion"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultVersion),
            KnowledgeBaseEmbeddingBatchSize = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_EMBEDDING_BATCH_SIZE"),
                section["KnowledgeBaseEmbeddingBatchSize"],
                HostedKnowledgeBaseEmbeddingDefaults.DefaultBatchSize),
            KnowledgeBaseQueryEmbeddingTimeoutMs = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_QUERY_EMBEDDING_TIMEOUT_MS"),
                section["KnowledgeBaseQueryEmbeddingTimeoutMs"],
                5000),
            KnowledgeBaseQueryEmbeddingRetries = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_QUERY_EMBEDDING_RETRIES"),
                section["KnowledgeBaseQueryEmbeddingRetries"],
                0),
            KnowledgeBaseQueryEmbeddingRetryDelayMs = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_QUERY_EMBEDDING_RETRY_DELAY_MS"),
                section["KnowledgeBaseQueryEmbeddingRetryDelayMs"],
                75),
            KnowledgeBaseQueryEmbeddingCacheEntries = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_QUERY_EMBEDDING_CACHE_ENTRIES"),
                section["KnowledgeBaseQueryEmbeddingCacheEntries"],
                512),
            KnowledgeBaseQueryEmbeddingCacheTtlMinutes = ParseInt(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_KB_QUERY_EMBEDDING_CACHE_TTL_MINUTES"),
                section["KnowledgeBaseQueryEmbeddingCacheTtlMinutes"],
                30),
            AllowImplicitLocalAdminBootstrap = ParseBool(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ALLOW_IMPLICIT_LOCAL_ADMIN_BOOTSTRAP"),
                section["AllowImplicitLocalAdminBootstrap"],
                false),
            AllowSeedTestUsers = ParseBool(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_ALLOW_TEST_USER_SEEDING"),
                section["AllowSeedTestUsers"],
                false),
            TrustForwardedHeaders = ParseBool(
                Environment.GetEnvironmentVariable("PHANTOM_WINDOWS_BACKEND_TRUST_FORWARDED_HEADERS"),
                section["TrustForwardedHeaders"],
                false)
        };
    }

    private static string ReadString(string envKey, string? configValue, string fallback)
    {
        var envValue = Environment.GetEnvironmentVariable(envKey);
        return string.IsNullOrWhiteSpace(envValue)
            ? (string.IsNullOrWhiteSpace(configValue) ? fallback : configValue.Trim())
            : envValue.Trim();
    }

    private static int ParseInt(string? envValue, string? configValue, int fallback)
    {
        if (int.TryParse(envValue, out var envParsed))
        {
            return envParsed;
        }

        if (int.TryParse(configValue, out var configParsed))
        {
            return configParsed;
        }

        return fallback;
    }

    private static decimal ParseDecimal(string? envValue, string? configValue, decimal fallback)
    {
        if (decimal.TryParse(envValue, out var envParsed))
        {
            return envParsed;
        }

        if (decimal.TryParse(configValue, out var configParsed))
        {
            return configParsed;
        }

        return fallback;
    }

    private static bool ParseBool(string? envValue, string? configValue, bool fallback)
    {
        if (bool.TryParse(envValue, out var envParsed))
        {
            return envParsed;
        }

        if (bool.TryParse(configValue, out var configParsed))
        {
            return configParsed;
        }

        return fallback;
    }

    private static void Require(ICollection<string> errors, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) errors.Add($"{name} is required.");
    }

    private static void RequireSecret(ICollection<string> errors, string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 32) errors.Add($"{name} must contain at least 32 characters.");
    }
}
