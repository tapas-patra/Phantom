using System.Net;
using System.Net.Mail;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Services;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class MagicLinkEmailService
{
    private readonly BackendOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MagicLinkEmailService> _logger;

    public MagicLinkEmailService(
        BackendOptions options,
        IServiceProvider serviceProvider,
        ILogger<MagicLinkEmailService> logger)
    {
        _options = options;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public (string Status, string Error) Send(string recipientEmail, string magicLinkUrl, DateTime expiresAtUtc)
    {
        var body = $"""
Use this sign-in link to return to Phantom:

{magicLinkUrl}

This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.
""";
        return SendMail(recipientEmail, "Your Phantom sign-in link", body);
    }

    public (string Status, string Error) SendEmailVerification(string recipientEmail, string verificationUrl, DateTime expiresAtUtc)
    {
        var body = $"""
Verify your Phantom account email:

{verificationUrl}

This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.
""";
        return SendMail(recipientEmail, "Verify your Phantom account", body);
    }

    public (string Status, string Error) SendAdminPasswordReset(string recipientEmail, string resetUrl, DateTime expiresAtUtc)
    {
        var body = $"""
Reset your Phantom admin password:

{resetUrl}

This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.
If you did not request this reset, ignore this email.
""";
        return SendMail(recipientEmail, "Reset your Phantom admin password", body);
    }

    public (string Status, string Error) SendUserPasswordReset(string recipientEmail, string resetUrl, DateTime expiresAtUtc)
    {
        var body = $"""
Reset your Phantom account password:

{resetUrl}

This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.
If you did not request this reset, ignore this email.
""";
        return SendMail(recipientEmail, "Reset your Phantom account password", body);
    }

    public string? GetDeliveryConfigurationError()
    {
        if (_options.HasGoogleOAuthClientSecrets && _options.HasSecretEncryptionKey)
        {
            var googleMailOAuth = _serviceProvider.GetRequiredService<GoogleMailOAuthService>();
            if (googleMailOAuth.HasRefreshTokenConfigured())
            {
                return null;
            }

            return "Gmail delivery is not authorized yet. Complete the Gmail OAuth bootstrap first.";
        }

        if (_options.IsSmtpConfigured)
        {
            return null;
        }

        return "Email delivery is not configured.";
    }

    private (string Status, string Error) SendMail(string recipientEmail, string subject, string body)
    {
        try
        {
            if (_options.HasGoogleOAuthClientSecrets && _options.HasSecretEncryptionKey)
            {
                SendViaGmailApi(recipientEmail, subject, body);
                return ("sent", string.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Gmail API email.");
            if (!_options.IsSmtpConfigured)
            {
                return ("send_failed", ex.Message);
            }
        }

        if (!_options.IsSmtpConfigured)
        {
            _logger.LogWarning("No email transport configured.");
            return ("not_sent", "Email transport not configured");
        }

        try
        {
            using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
            {
                EnableSsl = _options.SmtpEnableSsl
            };

            if (!string.IsNullOrWhiteSpace(_options.SmtpUsername))
            {
                client.Credentials = new NetworkCredential(_options.SmtpUsername, _options.SmtpPassword);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.SmtpFromEmail, _options.SmtpFromName),
                Subject = subject,
                Body = body,
                IsBodyHtml = false
            };
            message.To.Add(recipientEmail);
            client.Send(message);
            return ("sent", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email.");
            return ("send_failed", ex.Message);
        }
    }

    private void SendViaGmailApi(string recipientEmail, string subject, string body)
    {
        var googleMailOAuth = _serviceProvider.GetRequiredService<GoogleMailOAuthService>();
        var flow = googleMailOAuth.BuildFlow();
        var refreshToken = googleMailOAuth.GetRefreshToken();
        var token = flow.RefreshTokenAsync(
            userId: "gmail_sender",
            refreshToken: refreshToken,
            taskCancellationToken: CancellationToken.None).GetAwaiter().GetResult();

        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = GoogleCredential.FromAccessToken(token.AccessToken),
            ApplicationName = "Phantom"
        });
        var senderEmail = string.IsNullOrWhiteSpace(_options.SmtpFromEmail)
            ? "official.phantomai@gmail.com"
            : _options.SmtpFromEmail;

        var mime = $"""
From: Phantom <{senderEmail}>
To: {recipientEmail}
Subject: {subject}
Content-Type: text/plain; charset=utf-8

{body}
""";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(mime))
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        service.Users.Messages.Send(new Message
        {
            Raw = encoded
        }, "me").Execute();
    }
}
