using System.Net;
using System.Net.Mail;
using Phantom.WindowsApp.Backend.Infrastructure;

namespace Phantom.WindowsApp.Backend.Services;

public sealed class MagicLinkEmailService
{
    private readonly BackendOptions _options;
    private readonly ILogger<MagicLinkEmailService> _logger;

    public MagicLinkEmailService(BackendOptions options, ILogger<MagicLinkEmailService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public (string Status, string Error) Send(string recipientEmail, string magicLinkUrl, DateTime expiresAtUtc)
    {
        if (!_options.IsSmtpConfigured)
        {
            _logger.LogWarning("SMTP not configured. Magic link generated for {Email} but not sent.", recipientEmail);
            return ("not_sent", "SMTP not configured");
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
                Subject = "Your Phantom sign-in link",
                Body = $"""
Use this sign-in link to return to Phantom:

{magicLinkUrl}

This link expires at {expiresAtUtc:yyyy-MM-dd HH:mm:ss} UTC.
""",
                IsBodyHtml = false
            };
            message.To.Add(recipientEmail);
            client.Send(message);
            return ("sent", string.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send magic link to {Email}.", recipientEmail);
            return ("send_failed", ex.Message);
        }
    }
}
