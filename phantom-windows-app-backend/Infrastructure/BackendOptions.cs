using Microsoft.Extensions.Configuration;

namespace Phantom.WindowsApp.Backend.Infrastructure;

public sealed class BackendOptions
{
    public string DatabaseUrl { get; init; } = string.Empty;
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
    public string AdminApiKey { get; init; } = string.Empty;
    public string PublicWebsiteBaseUrl { get; init; } = string.Empty;
    public string SmtpHost { get; init; } = string.Empty;
    public int SmtpPort { get; init; } = 587;
    public string SmtpUsername { get; init; } = string.Empty;
    public string SmtpPassword { get; init; } = string.Empty;
    public string SmtpFromEmail { get; init; } = string.Empty;
    public string SmtpFromName { get; init; } = "Phantom";
    public bool SmtpEnableSsl { get; init; } = true;
    public string GoogleOAuthClientSecretsPath { get; init; } = string.Empty;
    public string GoogleOAuthClientSecretsJson { get; init; } = string.Empty;
    public string GoogleOAuthRedirectUri { get; init; } = string.Empty;
    public string SecretEncryptionKey { get; init; } = string.Empty;
    public string RazorpayKeyId { get; init; } = string.Empty;
    public string RazorpayKeySecret { get; init; } = string.Empty;
    public string RazorpayWebhookSecret { get; init; } = string.Empty;
    public string OtpProviderName { get; init; } = "2factor";
    public string OtpApiKey { get; init; } = string.Empty;
    public string OtpSenderId { get; init; } = string.Empty;
    public string OtpTemplateName { get; init; } = string.Empty;
    public string OtpSendUrlTemplate { get; init; } = string.Empty;
    public string OtpVerifyUrlTemplate { get; init; } = string.Empty;

    public bool HasAdminApiKey => !string.IsNullOrWhiteSpace(AdminApiKey);
    public bool IsSmtpConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost)
        && !string.IsNullOrWhiteSpace(SmtpFromEmail);
    public bool HasGoogleOAuthClientSecrets =>
        !string.IsNullOrWhiteSpace(GoogleOAuthClientSecretsPath)
        || !string.IsNullOrWhiteSpace(GoogleOAuthClientSecretsJson);
    public bool HasSecretEncryptionKey => !string.IsNullOrWhiteSpace(SecretEncryptionKey);
    public bool HasRazorpayCredentials =>
        !string.IsNullOrWhiteSpace(RazorpayKeyId)
        && !string.IsNullOrWhiteSpace(RazorpayKeySecret);
    public bool HasRazorpayWebhookSecret => !string.IsNullOrWhiteSpace(RazorpayWebhookSecret);
    public bool HasOtpApiKey => !string.IsNullOrWhiteSpace(OtpApiKey);

    public static BackendOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection("PhantomBackend");
        return new BackendOptions
        {
            DatabaseUrl = ReadString(
                "PHANTOM_WINDOWS_BACKEND_DATABASE_URL",
                section["DatabaseUrl"],
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
            AdminApiKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_ADMIN_API_KEY",
                section["AdminApiKey"],
                string.Empty),
            PublicWebsiteBaseUrl = ReadString(
                "PHANTOM_PUBLIC_WEBSITE_BASE_URL",
                section["PublicWebsiteBaseUrl"],
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
            GoogleOAuthRedirectUri = ReadString(
                "PHANTOM_WINDOWS_BACKEND_GOOGLE_OAUTH_REDIRECT_URI",
                section["GoogleOAuthRedirectUri"],
                string.Empty),
            SecretEncryptionKey = ReadString(
                "PHANTOM_WINDOWS_BACKEND_SECRET_ENCRYPTION_KEY",
                section["SecretEncryptionKey"],
                string.Empty),
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
                "https://2factor.in/API/V1/{apiKey}/SMS/VERIFY3/{sessionId}/{otp}")
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
}
