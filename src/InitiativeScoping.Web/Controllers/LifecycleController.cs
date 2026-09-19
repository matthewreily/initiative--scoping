using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Access;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Services;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Initiative lifecycle: activation (direct or via Admin-approved request), status transitions, baselines and the re-baseline workflow.</summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class LifecycleController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, ActivationNotifier notifier) : Controller
{
    private const string Entity = nameof(Initiative);

    // ----- Activation / status -----

    /// <summary>Admins activate directly; a pending activation request (if any) is recorded as approved by them.</summary>
    [HttpPost("Initiatives/{id:int}/Activate")]
    public async Task<IActionResult> Activate(int id, string? reason, CancellationToken ct)
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

        if (!InitiativeAccess.CanApproveActivation(currentUser))
        {
            return RedirectWithError("Activation needs Administrator approval: use Request activation.", id);
        }

        var readiness = ActivationReadiness(initiative);
        if (readiness is not null)
        {
            return RedirectWithError(readiness, id);
        }

        var pending = initiative.PendingActivation;
        if (pending is not null)
        {
            Decide(pending, ActivationRequestStatus.Approved, "Activated directly");
        }

        var result = await TryActivateAsync(initiative, string.IsNullOrWhiteSpace(reason) ? "Activation" : reason.Trim(), pending?.Id, ct);
        return result.Baseline is null
            ? RedirectWithError("Cannot activate: " + result.Error, id)
            : RedirectWithSuccess($"Initiative activated. Forecast baseline v{result.Baseline.Version} captured ({result.Baseline.TotalHours:N1} h, {result.Baseline.TotalCost:C0}).", id);
    }

    [HttpPost("Initiatives/{id:int}/RequestActivation")]
    public async Task<IActionResult> RequestActivation(int id, string? reason, CancellationToken ct)
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

        var readiness = ActivationReadiness(initiative);
        if (readiness is not null)
        {
            return RedirectWithError(readiness, id);
        }

        if (initiative.PendingActivation is not null)
        {
            return RedirectWithError("An activation request is already awaiting approval.", id);
        }

        var forecast = ForecastCalculator.Calculate(initiative, await LoadRateCardsAsync(ct));
        var blockers = InitiativeLifecycle.BaselineBlockers(initiative, forecast);
        if (blockers.Count > 0)
        {
            return RedirectWithError("Cannot request activation: " + string.Join(" ", blockers), id);
        }

        var request = new ActivationRequest
        {
            InitiativeId = id,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            RequestedBy = currentUser.UserId,
            RequestedAt = clock.GetUtcNow()
        };
        initiative.ActivationRequests.Add(request);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, id, AuditActions.ActivationRequest, new { RequestId = request.Id, request.Reason });
        await db.SaveChangesAsync(ct);
        await notifier.NotifyRequestedAsync(initiative, request, currentUser.DisplayName, DetailsUrl(id), ct);
        return RedirectWithSuccess("Activation requested; an Administrator must approve before the initiative becomes Active.", id);
    }

    /// <summary>Approving activates the initiative and captures baseline v1 in the same step; rejecting leaves it in Draft.</summary>
    [HttpPost("Initiatives/{id:int}/DecideActivation")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> DecideActivation(int id, int requestId, bool approve, string? note, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        var request = initiative?.ActivationRequests.FirstOrDefault(r => r.Id == requestId);
        if (initiative is null || request is null)
        {
            return NotFound();
        }

        if (request.Status != ActivationRequestStatus.Pending)
        {
            return RedirectWithError($"Request is already {request.Status}.", id);
        }

        var decisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (!approve)
        {
            Decide(request, ActivationRequestStatus.Rejected, decisionNote);
            await db.SaveChangesAsync(ct);
            await notifier.NotifyDecidedAsync(initiative, request, currentUser.DisplayName, DetailsUrl(id), ct);
            return RedirectWithSuccess("Activation request rejected; the initiative stays in Draft.", id);
        }

        var readiness = ActivationReadiness(initiative);
        if (readiness is not null)
        {
            return RedirectWithError(readiness, id);
        }

        Decide(request, ActivationRequestStatus.Approved, decisionNote);
        var result = await TryActivateAsync(initiative, request.Reason ?? "Activation", request.Id, ct);
        if (result.Baseline is null)
        {
            return RedirectWithError("Cannot approve: " + result.Error + " The request stays pending until the Owner fixes the plan.", id);
        }

        await notifier.NotifyDecidedAsync(initiative, request, currentUser.DisplayName, DetailsUrl(id), ct);
        return RedirectWithSuccess($"Activation approved. Forecast baseline v{result.Baseline.Version} captured ({result.Baseline.TotalHours:N1} h, {result.Baseline.TotalCost:C0}).", id);
    }

    [HttpPost("Initiatives/{id:int}/WithdrawActivation")]
    public async Task<IActionResult> WithdrawActivation(int id, CancellationToken ct)
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

        var pending = initiative.PendingActivation;
        if (pending is null)
        {
            return RedirectWithError("No activation request is awaiting approval.", id);
        }

        Decide(pending, ActivationRequestStatus.Withdrawn, null);
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Activation request withdrawn.", id);
    }

    [HttpPost("Initiatives/{id:int}/ChangeStatus")]
    public async Task<IActionResult> ChangeStatus(int id, InitiativeStatus to, string? note, CancellationToken ct)
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

        if (initiative.IsScenario)
        {
            return RedirectWithError("Scenarios stay Draft; promote or delete them instead of changing status.", id);
        }

        if (to == InitiativeStatus.Active && initiative.Status == InitiativeStatus.Draft)
        {
            return RedirectWithError("Use Activate to move a Draft initiative to Active.", id);
        }

        if (!InitiativeLifecycle.CanTransition(initiative.Status, to))
        {
            return RedirectWithError($"Cannot move from {initiative.Status} to {to}.", id);
        }

        var open = initiative.OpenRebaseline;
        if (open is not null)
        {
            if (to != InitiativeStatus.Cancelled)
            {
                return RedirectWithError("Finalize or withdraw the open re-baseline request first.", id);
            }

            open.Status = RebaselineStatus.Withdrawn;
            open.DecidedBy = currentUser.UserId;
            open.DecidedAt = clock.GetUtcNow();
            open.DecisionNote = "Initiative cancelled";
        }

        if (initiative.PendingActivation is { } pendingActivation)
        {
            Decide(pendingActivation, ActivationRequestStatus.Withdrawn, "Initiative cancelled");
        }

        var from = initiative.Status;
        initiative.Status = to;
        audit.Record(Entity, id, AuditActions.StatusChange, new { From = from, To = to, Note = note });
        await db.SaveChangesAsync(ct);
        AppTelemetry.StatusChanges.Add(1, new KeyValuePair<string, object?>("to", to.ToString()));
        return RedirectWithSuccess($"Status changed from {from} to {to}.", id);
    }

    [HttpPost("Initiatives/{id:int}/Reopen")]
    public async Task<IActionResult> Reopen(int id, string? note, CancellationToken ct)
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

        if (initiative.Status != InitiativeStatus.Cancelled)
        {
            return RedirectWithError("Only Cancelled initiatives can be reopened.", id);
        }

        var to = InitiativeLifecycle.ReopenTarget(initiative.Baselines.Count > 0);
        initiative.Status = to;
        audit.Record(Entity, id, AuditActions.StatusChange, new { From = InitiativeStatus.Cancelled, To = to, Note = note, Reopened = true });
        await db.SaveChangesAsync(ct);
        AppTelemetry.StatusChanges.Add(1, new KeyValuePair<string, object?>("to", to.ToString()));
        return RedirectWithSuccess(to == InitiativeStatus.Draft
            ? "Initiative reopened as Draft; it can be edited and activated again."
            : $"Initiative reopened On hold (baseline v{initiative.CurrentBaseline?.Version} kept); use Change status → Active to resume.", id);
    }

    // ----- Baselines -----

    [HttpGet("Initiatives/{id:int}/Baselines")]
    public async Task<IActionResult> Baselines(int id, int? version, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        var baselines = await db.ForecastBaselines.Include(b => b.Lines).Include(b => b.NonLaborLines)
            .Where(b => b.InitiativeId == id).AsNoTracking().ToListAsync(ct);
        baselines = baselines.OrderByDescending(b => b.Version).ToList();
        var selected = version is null ? baselines.FirstOrDefault(b => b.IsCurrent) ?? baselines.FirstOrDefault()
            : baselines.FirstOrDefault(b => b.Version == version);
        if (version is not null && selected is null)
        {
            return NotFound();
        }

        var previous = selected is null ? null : baselines.FirstOrDefault(b => b.Version < selected.Version);
        var forecast = ForecastCalculator.Calculate(initiative, await LoadRateCardsAsync(ct));
        var notes = selected is null ? new List<InitiativeNote>() : (await db.InitiativeNotes.AsNoTracking()
            .Where(n => n.ForecastBaselineId == selected.Id).ToListAsync(ct))
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).ToList();

        return View(new BaselinesModel
        {
            Initiative = initiative,
            Baselines = baselines,
            Selected = selected,
            Previous = previous,
            LiveForecast = forecast,
            Lines = selected is null ? [] : BaselineLines(selected, previous),
            Requests = initiative.RebaselineRequests.OrderByDescending(r => r.Id).ToList(),
            Notes = notes,
            CanManage = InitiativeAccess.CanManage(currentUser, initiative),
            CanApprove = InitiativeAccess.CanApproveRebaseline(currentUser),
            CanAddNote = InitiativeAccess.CanAddNote(currentUser)
        });
    }

    // ----- Re-baseline workflow -----

    [HttpPost("Initiatives/{id:int}/RequestRebaseline")]
    public async Task<IActionResult> RequestRebaseline(int id, string reason, CancellationToken ct)
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
            return RedirectWithError("Only Active initiatives can be re-baselined.", id);
        }

        if (initiative.OpenRebaseline is not null)
        {
            return RedirectWithError("A re-baseline request is already open.", id);
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return RedirectWithError("A reason is required to request a re-baseline.", id);
        }

        var request = new RebaselineRequest
        {
            InitiativeId = id, Reason = reason.Trim(), RequestedBy = currentUser.UserId, RequestedAt = clock.GetUtcNow()
        };
        initiative.RebaselineRequests.Add(request);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, id, AuditActions.RebaselineRequest, new { RequestId = request.Id, request.Reason });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Re-baseline requested; an Administrator must approve before scope can change.", id);
    }

    [HttpPost("Initiatives/{id:int}/DecideRebaseline")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> DecideRebaseline(int id, int requestId, bool approve, string? note, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        var request = initiative?.RebaselineRequests.FirstOrDefault(r => r.Id == requestId);
        if (initiative is null || request is null)
        {
            return NotFound();
        }

        if (request.Status != RebaselineStatus.Pending)
        {
            return RedirectWithError($"Request is already {request.Status}.", id);
        }

        request.Status = approve ? RebaselineStatus.Approved : RebaselineStatus.Rejected;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        audit.Record(Entity, id, AuditActions.RebaselineDecision, new { RequestId = requestId, request.Status, request.DecisionNote });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess(approve
            ? "Re-baseline approved. Scope is unlocked until the Owner finalizes the new baseline."
            : "Re-baseline request rejected.", id);
    }

    [HttpPost("Initiatives/{id:int}/WithdrawRebaseline")]
    public async Task<IActionResult> WithdrawRebaseline(int id, CancellationToken ct)
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

        var open = initiative.OpenRebaseline;
        if (open is null)
        {
            return RedirectWithError("No open re-baseline request.", id);
        }

        open.Status = RebaselineStatus.Withdrawn;
        open.DecidedBy = currentUser.UserId;
        open.DecidedAt = clock.GetUtcNow();
        audit.Record(Entity, id, AuditActions.RebaselineDecision, new { RequestId = open.Id, open.Status });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Re-baseline request withdrawn; scope is locked again against the current baseline.", id);
    }

    [HttpPost("Initiatives/{id:int}/FinalizeRebaseline")]
    public async Task<IActionResult> FinalizeRebaseline(int id, CancellationToken ct)
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

        var open = initiative.OpenRebaseline;
        if (open?.Status != RebaselineStatus.Approved)
        {
            return RedirectWithError("No approved re-baseline request to finalize.", id);
        }

        var forecast = ForecastCalculator.Calculate(initiative, await LoadRateCardsAsync(ct));
        var blockers = InitiativeLifecycle.BaselineBlockers(initiative, forecast);
        if (blockers.Count > 0)
        {
            return RedirectWithError("Cannot baseline: " + string.Join(" ", blockers), id);
        }

        var baseline = BaselineSnapshot.Create(initiative, forecast, currentUser.UserId, clock.GetUtcNow(), open.Reason);
        open.Status = RebaselineStatus.Completed;
        open.ResultingBaseline = baseline;
        audit.Record(Entity, id, AuditActions.Baseline, BaselineDiff(baseline, open.Id));
        ChangeRequestsController.Implement(initiative, open, baseline, forecast, audit);
        await db.SaveChangesAsync(ct);
        AppTelemetry.BaselinesCaptured.Add(1, new KeyValuePair<string, object?>("kind", "rebaseline"));
        return RedirectWithSuccess($"Forecast baseline v{baseline.Version} captured ({baseline.TotalHours:N1} h, {baseline.TotalCost:C0}). Scope is locked.", id);
    }

    /// <summary>Administrator queue of pending activation and re-baseline requests.</summary>
    [HttpGet("Approvals")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var activations = await db.ActivationRequests.Include(r => r.Initiative)
            .Where(r => r.Status == ActivationRequestStatus.Pending).AsNoTracking().ToListAsync(ct);
        var rebaselines = await db.RebaselineRequests.Include(r => r.Initiative)
            .Where(r => r.Status == RebaselineStatus.Pending).AsNoTracking().ToListAsync(ct);
        var changes = await db.ChangeRequests.Include(r => r.Initiative)
            .Where(r => r.Status == ChangeRequestStatus.Pending).AsNoTracking().ToListAsync(ct);
        return View(new ApprovalsModel
        {
            Activations = activations.OrderBy(r => r.Id).ToList(),
            Rebaselines = rebaselines.OrderBy(r => r.Id).ToList(),
            Changes = changes.OrderBy(r => r.Id).ToList()
        });
    }

    // ----- Helpers -----

    private static string? ActivationReadiness(Initiative initiative) =>
        initiative.IsScenario ? "Scenarios cannot be activated; promote the scenario to the live plan first."
        : initiative.Status != InitiativeStatus.Draft ? $"Only Draft initiatives can be activated (current status: {initiative.Status})."
        : null;

    private void Decide(ActivationRequest request, ActivationRequestStatus status, string? note)
    {
        request.Status = status;
        request.DecidedBy = currentUser.UserId;
        request.DecidedAt = clock.GetUtcNow();
        request.DecisionNote = note;
        audit.Record(Entity, request.InitiativeId, AuditActions.ActivationDecision, new { RequestId = request.Id, request.Status, request.DecisionNote });
    }

    /// <summary>Freezes the forecast as baseline v1 and moves the Draft initiative to Active; returns the blockers instead when the plan is not ready.</summary>
    private async Task<(ForecastBaseline? Baseline, string? Error)> TryActivateAsync(Initiative initiative, string reason, int? requestId, CancellationToken ct)
    {
        var forecast = ForecastCalculator.Calculate(initiative, await LoadRateCardsAsync(ct));
        var blockers = InitiativeLifecycle.BaselineBlockers(initiative, forecast);
        if (blockers.Count > 0)
        {
            return (null, string.Join(" ", blockers));
        }

        var baseline = BaselineSnapshot.Create(initiative, forecast, currentUser.UserId, clock.GetUtcNow(), reason);
        initiative.Status = InitiativeStatus.Active;
        audit.Record(Entity, initiative.Id, AuditActions.StatusChange, new { From = InitiativeStatus.Draft, To = InitiativeStatus.Active, RequestId = requestId });
        audit.Record(Entity, initiative.Id, AuditActions.Baseline, BaselineDiff(baseline, requestId));
        await db.SaveChangesAsync(ct);
        AppTelemetry.StatusChanges.Add(1, new KeyValuePair<string, object?>("to", nameof(InitiativeStatus.Active)));
        AppTelemetry.BaselinesCaptured.Add(1, new KeyValuePair<string, object?>("kind", "activation"));
        return (baseline, null);
    }

    private string DetailsUrl(int id) =>
        Url.Action(nameof(InitiativesController.Details), "Initiatives", new { id }, Request.Scheme, Request.Host.Value)!;

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct) =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations).ThenInclude(a => a.BusinessUnit)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourceType)
            .Include(i => i.Allocations).ThenInclude(a => a.Seniority)
            .Include(i => i.Allocations).ThenInclude(a => a.Vendor)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourcingClass)
            .Include(i => i.Allocations).ThenInclude(a => a.People).ThenInclude(p => p.Person)
            .Include(i => i.NonLaborCosts)
            .Include(i => i.Baselines)
            .Include(i => i.RebaselineRequests)
            .Include(i => i.ChangeRequests)
            .Include(i => i.ActivationRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) => db.PricingRateCardsAsync(ct);

    private static object BaselineDiff(ForecastBaseline b, int? requestId = null) =>
        new { b.Version, b.TotalHours, b.TotalCost, LineCount = b.Lines.Count, b.Reason, RequestId = requestId };

    private static List<BaselineLineRow> BaselineLines(ForecastBaseline selected, ForecastBaseline? previous)
    {
        static string Key(ForecastBaselineLine l) => $"{l.PhaseId}|{l.BusinessUnitId}|{l.ResourceTypeId}|{l.SeniorityId}|{l.Location}|{l.ResourcingClassId}|{l.VendorId}|{l.PersonId}";
        var prev = (previous?.Lines ?? []).GroupBy(Key).ToDictionary(g => g.Key, g => (Hours: g.Sum(l => l.Hours), Cost: g.Sum(l => l.Cost)));
        var rows = selected.Lines.GroupBy(Key).Select(g =>
        {
            var first = g.First();
            prev.Remove(g.Key, out var p);
            return new BaselineLineRow(
                first.PhaseName, first.BusinessUnitName, first.VendorName, first.ResourceTypeName,
                first.SeniorityName, first.Location, first.ResourcingClassName,
                g.Sum(l => l.Hours), first.HourlyRate, g.Sum(l => l.Cost),
                previous is null ? null : g.Sum(l => l.Hours) - p.Hours,
                previous is null ? null : g.Sum(l => l.Cost) - p.Cost) { Person = first.PersonName };
        }).ToList();

        // Lines that existed in the previous version but were removed.
        foreach (var (key, p) in prev)
        {
            var l = previous!.Lines.First(x => Key(x) == key);
            rows.Add(new BaselineLineRow(
                l.PhaseName, l.BusinessUnitName, l.VendorName, l.ResourceTypeName,
                l.SeniorityName, l.Location, l.ResourcingClassName, 0m, l.HourlyRate, 0m, -p.Hours, -p.Cost) { Person = l.PersonName });
        }

        return rows.OrderBy(r => r.Phase).ThenBy(r => r.ResourceType).ThenBy(r => r.Seniority).ToList();
    }

    private IActionResult RedirectWithSuccess(string message, int id)
    {
        TempData["Success"] = message;
        return RedirectToAction(nameof(InitiativesController.Details), "Initiatives", new { id });
    }

    private IActionResult RedirectWithError(string message, int id)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(InitiativesController.Details), "Initiatives", new { id });
    }
}
