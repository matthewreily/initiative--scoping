using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Access;

/// <summary>E-mails administrators when activation is requested and the requester when an Admin decides.</summary>
public class ActivationNotifier(AppDbContext db, IEmailSender email, AccessRequestNotifier admins)
{
    public async Task NotifyRequestedAsync(Initiative initiative, ActivationRequest request, string requestedByName, string detailsUrl, CancellationToken ct)
    {
        if (!email.IsEnabled)
        {
            return;
        }

        var recipients = await admins.RecipientsAsync(ct);
        if (recipients.Count == 0)
        {
            return;
        }

        var body = $"""
            {requestedByName} has requested activation of "{initiative.Name}".

            {(string.IsNullOrWhiteSpace(request.Reason) ? "No note was provided." : "Note: " + request.Reason)}

            Approving freezes the current forecast as baseline v1 and locks scope. Approve or reject from the initiative page or under Approvals:
            {detailsUrl}
            """;
        await email.SendAsync(new EmailMessage(recipients, $"Activation requested: {initiative.Name}", body), ct);
    }

    public async Task NotifyDecidedAsync(Initiative initiative, ActivationRequest request, string decidedByName, string detailsUrl, CancellationToken ct)
    {
        if (!email.IsEnabled)
        {
            return;
        }

        var requester = await db.UserAccounts
            .Where(a => a.ObjectId == request.RequestedBy && a.Status == UserAccountStatus.Active)
            .Select(a => a.Email)
            .FirstOrDefaultAsync(ct);
        if (requester is null || !requester.Contains('@'))
        {
            return;
        }

        var approved = request.Status == ActivationRequestStatus.Approved;
        var body = $"""
            {decidedByName} has {(approved ? "approved" : "rejected")} your request to activate "{initiative.Name}".

            {(string.IsNullOrWhiteSpace(request.DecisionNote) ? "No note was provided." : "Note: " + request.DecisionNote)}

            {(approved ? "The initiative is now Active and baseline v1 has been captured." : "The initiative stays in Draft; update the plan and request activation again when ready.")}
            {detailsUrl}
            """;
        await email.SendAsync(new EmailMessage([requester], $"Activation {(approved ? "approved" : "rejected")}: {initiative.Name}", body), ct);
    }
}
