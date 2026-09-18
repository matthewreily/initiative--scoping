using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Access;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using InitiativeScoping.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>
/// Formal change control on Active initiatives: Owners raise a change request (scope / schedule / cost / resource)
/// with a reason and estimated impact; Admins approve or reject. Approval opens (or joins) the re-baseline, and
/// finalising that re-baseline marks the change Implemented against the new baseline version.
/// </summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class ChangeRequestsController(
    AppDbContext db,
    ICurrentUser currentUser,
    IAuditLog audit,
    TimeProvider clock) : Controller
{
    private const string Entity = nameof(Initiative);
    private const string Fragment = "pane-changes";

    [HttpPost("Initiatives/{id:int}/ChangeRequests")]
    public async Task<IActionResult> Raise(int id, ChangeRequestEditModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanManage(currentUser, initiative))
        {
            return Forbid();
        }

        if (initiative.Status != InitiativeStatus.Active)
        {
            return RedirectWithError("Change requests apply to Active initiatives; edit a Draft directly.", id);
        }

        if (string.IsNullOrWhiteSpace(model.Title) || string.IsNullOrWhiteSpace(model.Description) || string.IsNullOrWhiteSpace(model.Reason))
        {
            return RedirectWithError("Title, description and reason are required to raise a change request.", id);
        }

        if (model.Title.Trim().Length > 200 || model.Description.Trim().Length > 4000 || model.Reason.Trim().Length > 2000)
        {
            return RedirectWithError("Title (200), description (4000) or reason (2000) is too long.", id);
        }

        if (model.Type == ChangeRequestType.Schedule && model.ProposedTargetEnd is null)
        {
            return RedirectWithError("A schedule change needs a proposed target end.", id);
        }

        var forecast = ForecastCalculator.Calculate(initiative, await db.PricingRateCardsAsync(ct));
        var request = new ChangeRequest
        {
            InitiativeId = id,
            Number = (initiative.ChangeRequests.Count == 0 ? 0 : initiative.ChangeRequests.Max(c => c.Number)) + 1,
            Type = model.Type,
            Title = model.Title.Trim(),
            Description = model.Description.Trim(),
            Reason = model.Reason.Trim(),
            EstimatedCostImpact = model.EstimatedCostImpact,
            EstimatedHoursImpact = model.EstimatedHoursImpact,
            ProposedTargetEnd = model.ProposedTargetEnd,
            BaselineVersionBefore = initiative.CurrentBaseline?.Version,
            ForecastHoursBefore = forecast.TotalHours,
            ForecastCostBefore = forecast.TotalCost,
            TargetEndBefore = initiative.TargetEnd,
            RequestedBy = currentUser.UserId,
            RequestedAt = clock.GetUtcNow()
        };
        initiative.ChangeRequests.Add(request);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, id, AuditActions.ChangeRequest, new { RequestId = request.Id, request.Code, request.Type, request.Title, request.EstimatedCostImpact, request.EstimatedHoursImpact, request.ProposedTargetEnd });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"{request.Code} raised; an Administrator must approve before the plan can change.", id);
    }

    [HttpPost("Initiatives/{id:int}/ChangeRequests/{requestId:int}/Decide")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Decide(int id, int requestId, bool approve, string? note, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        var request = initiative?.ChangeRequests.FirstOrDefault(c => c.Id == requestId);
        if (initiative is null || request is null)
        {
            return NotFound();
        }

        if (request.Status != ChangeRequestStatus.Pending)
        {
            return RedirectWithError($"{request.Code} is already {request.Status}.", id);
        }

        var now = clock.GetUtcNow();
        request.Status = approve ? ChangeRequestStatus.Approved : ChangeRequestStatus.Rejected;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = now;
        request.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        string message;
        if (approve && initiative.Status == InitiativeStatus.Active)
        {
            var open = initiative.OpenRebaseline;
            if (open is null)
            {
                open = new RebaselineRequest
                {
                    InitiativeId = id,
                    Reason = $"{request.Code}: {request.Title}",
                    RequestedBy = request.RequestedBy,
                    RequestedAt = request.RequestedAt,
                    Status = RebaselineStatus.Approved,
                    DecidedBy = currentUser.UserId,
                    DecidedAt = now,
                    DecisionNote = $"Approved with {request.Code}."
                };
                initiative.RebaselineRequests.Add(open);
                audit.Record(Entity, id, AuditActions.RebaselineRequest, new { open.Reason, ChangeRequest = request.Code });
                audit.Record(Entity, id, AuditActions.RebaselineDecision, new { open.Status, ChangeRequest = request.Code });
                message = $"{request.Code} approved. Scope is unlocked: apply the change, then finalize baseline v{(initiative.CurrentBaseline?.Version ?? 0) + 1}.";
            }
            else if (open.Status == RebaselineStatus.Pending)
            {
                open.Status = RebaselineStatus.Approved;
                open.DecidedBy = currentUser.UserId;
                open.DecidedAt = now;
                open.DecisionNote = $"Approved with {request.Code}.";
                audit.Record(Entity, id, AuditActions.RebaselineDecision, new { RequestId = open.Id, open.Status, ChangeRequest = request.Code });
                message = $"{request.Code} approved together with the pending re-baseline. Scope is unlocked until the Owner finalizes the new baseline.";
            }
            else
            {
                message = $"{request.Code} approved; it will be implemented by the open re-baseline when the Owner finalizes it.";
            }

            request.RebaselineRequest = open;
        }
        else
        {
            message = approve ? $"{request.Code} approved." : $"{request.Code} rejected.";
        }

        audit.Record(Entity, id, AuditActions.ChangeRequestDecision, new { RequestId = request.Id, request.Code, request.Status, request.DecisionNote });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess(message, id);
    }

    [HttpPost("Initiatives/{id:int}/ChangeRequests/{requestId:int}/Withdraw")]
    public async Task<IActionResult> Withdraw(int id, int requestId, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        var request = initiative?.ChangeRequests.FirstOrDefault(c => c.Id == requestId);
        if (initiative is null || request is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanManage(currentUser, initiative))
        {
            return Forbid();
        }

        if (request.Status != ChangeRequestStatus.Pending)
        {
            return RedirectWithError($"Only pending change requests can be withdrawn ({request.Code} is {request.Status}).", id);
        }

        request.Status = ChangeRequestStatus.Withdrawn;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = clock.GetUtcNow();
        audit.Record(Entity, id, AuditActions.ChangeRequestDecision, new { RequestId = request.Id, request.Code, request.Status });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"{request.Code} withdrawn.", id);
    }

    /// <summary>
    /// Marks every approved change request on the initiative Implemented against the baseline just captured,
    /// recording the after-values so the realised impact can be compared with the estimate.
    /// </summary>
    public static void Implement(Initiative initiative, RebaselineRequest rebaseline, ForecastBaseline baseline, ForecastResult forecast, IAuditLog audit)
    {
        foreach (var request in initiative.ChangeRequests.Where(c => c.Status == ChangeRequestStatus.Approved))
        {
            request.Status = ChangeRequestStatus.Implemented;
            request.RebaselineRequest = rebaseline;
            request.ResultingBaseline = baseline;
            request.ForecastHoursAfter = forecast.TotalHours;
            request.ForecastCostAfter = forecast.TotalCost;
            request.TargetEndAfter = initiative.TargetEnd;
            audit.Record(Entity, initiative.Id, AuditActions.ChangeRequestImplemented,
                new { RequestId = request.Id, request.Code, BaselineVersion = baseline.Version, request.EstimatedCostImpact, request.ActualCostImpact, request.EstimatedHoursImpact, request.ActualHoursImpact });
        }
    }

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct) =>
        db.Initiatives
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations).ThenInclude(a => a.BusinessUnit)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourceType)
            .Include(i => i.Allocations).ThenInclude(a => a.Seniority)
            .Include(i => i.Allocations).ThenInclude(a => a.Vendor)
            .Include(i => i.Allocations).ThenInclude(a => a.People).ThenInclude(p => p.Person)
            .Include(i => i.NonLaborCosts)
            .Include(i => i.Baselines)
            .Include(i => i.RebaselineRequests)
            .Include(i => i.ChangeRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private IActionResult RedirectWithSuccess(string message, int id)
    {
        TempData["Success"] = message;
        return RedirectToAction(nameof(InitiativesController.Details), "Initiatives", new { id }, Fragment);
    }

    private IActionResult RedirectWithError(string message, int id)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(InitiativesController.Details), "Initiatives", new { id }, Fragment);
    }
}
