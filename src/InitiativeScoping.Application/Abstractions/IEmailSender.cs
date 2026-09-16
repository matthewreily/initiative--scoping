namespace InitiativeScoping.Application.Abstractions;

public record EmailMessage(IReadOnlyCollection<string> To, string Subject, string TextBody);

/// <summary>Outbound e-mail. Implementations must not throw for a misconfigured or unreachable mail server; callers treat delivery as best-effort.</summary>
public interface IEmailSender
{
    /// <summary>True when messages will actually be delivered (a mail server is configured).</summary>
    bool IsEnabled { get; }

    Task SendAsync(EmailMessage message, CancellationToken ct);
}
