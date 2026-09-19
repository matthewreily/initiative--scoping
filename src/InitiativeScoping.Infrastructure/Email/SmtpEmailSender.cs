using InitiativeScoping.Application.Abstractions;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace InitiativeScoping.Infrastructure.Email;

public class EmailOptions
{
    public const string Section = "Email";

    /// <summary>SMTP server; leave empty to disable outbound e-mail.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary>Upgrade to TLS with STARTTLS (port 587); false uses implicit TLS on port 465 or plain text on 25.</summary>
    public bool UseStartTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>Sender address; defaults to <see cref="Username"/>.</summary>
    public string? From { get; set; }
    public string FromName { get; set; } = "Scopewell";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(FromAddress);

    public string? FromAddress => string.IsNullOrWhiteSpace(From) ? Username : From;
}

/// <summary>Sends mail over SMTP when <c>Email:Host</c> is configured; otherwise logs and drops the message.</summary>
public class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public bool IsEnabled => options.Value.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        if (message.To.Count == 0)
        {
            return;
        }

        if (o.Host is not { } host || o.FromAddress is not { } from || !o.IsConfigured)
        {
            logger.LogInformation("E-mail not configured (Email:Host); dropping \"{Subject}\" to {Recipients}", message.Subject, string.Join(", ", message.To));
            return;
        }

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(o.FromName, from));
            foreach (var to in message.To)
            {
                mime.To.Add(MailboxAddress.Parse(to));
            }

            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.TextBody };

            using var client = new SmtpClient();
            var security = o.UseStartTls ? SecureSocketOptions.StartTls : o.Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.Auto;
            await client.ConnectAsync(host, o.Port, security, ct);
            if (!string.IsNullOrWhiteSpace(o.Username))
            {
                await client.AuthenticateAsync(o.Username, o.Password ?? string.Empty, ct);
            }

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to send e-mail \"{Subject}\" to {Recipients}", message.Subject, string.Join(", ", message.To));
        }
    }
}
