using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InitiativeScoping.Infrastructure.Access;

/// <summary>E-mails every Active administrator (and any bootstrap admin configured by e-mail) when someone asks for access.</summary>
public class AccessRequestNotifier(AppDbContext db, IEmailSender email, IOptions<UserAccessOptions> options)
{
    public async Task NotifyAsync(UserAccount request, string usersPageUrl, CancellationToken ct)
    {
        if (!email.IsEnabled)
        {
            return;
        }

        var recipients = await RecipientsAsync(ct);
        if (recipients.Count == 0)
        {
            return;
        }

        var body = $"""
            {request.DisplayName} ({request.Email}) has requested access to Scopewell.

            {(string.IsNullOrWhiteSpace(request.Note) ? "No note was provided." : "Note: " + request.Note)}

            Approve or decline the request under Admin → Users:
            {usersPageUrl}
            """;
        await email.SendAsync(new EmailMessage(recipients, $"Access request from {request.DisplayName}", body), ct);
    }

    public async Task<IReadOnlyCollection<string>> RecipientsAsync(CancellationToken ct)
    {
        var admins = await db.UserAccounts
            .Where(a => a.Role == AppRole.Admin && a.Status == UserAccountStatus.Active)
            .Select(a => a.Email)
            .ToListAsync(ct);
        return admins
            .Concat(options.Value.BootstrapAdmins)
            .Where(e => e.Contains('@'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
