using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Services;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Initiative lifecycle: activation, status transitions, baselines and the re-baseline workflow.</summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class LifecycleController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock) : Controller
{
    private const string Entity = nameof(Initiative);

    // ----- Activation / status -----

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

        if (initiative.Status != InitiativeStatus.Draft)
        {
            return RedirectWithError($"Only Draft initiatives can be activated (current status: {initiative.Status}).", id);
        }

        var forecast = ForecastCalculator.Calculate(initiative, await LoadRateCardsAsync(ct));
        var blockers = InitiativeLifecycle.BaselineBlockers(initiative, forecast);
        if (blockers.Count > 0)
        {
            return RedirectWithError("Cannot activate: " + string.Join(" ", blockers), id);
        }

        var baseline = BaselineSnapshot.Create(initiative, forecast, currentUser.UserId, clock.GetUtcNow(),
            string.IsNullOrWhiteSpace(reason) ? "Activation" : reason.Trim());
        initiative.Status = InitiativeStatus.Active;
        audit.Record(Entity, id, AuditActions.StatusChange, new { From = InitiativeStatus.Draft, To = InitiativeStatus.Active });
        audit.Record(Entity, id, AuditActions.Baseline, BaselineDiff(baseline));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Initiative activated. Forecast baseline v{baseline.Version} captured ({baseline.TotalHours:N1} h, {baseline.TotalCost:C0}).", id);
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

        var from = initiative.Status;
        initiative.Status = to;
        audit.Record(Entity, id, AuditActions.StatusChange, new { From = from, To = to, Note = note });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Status changed from {from} to {to}.", id);
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

        var baselines = await db.ForecastBaselines.Include(b => b.Lines)
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
        var typeNames = await db.ResourceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var phaseNames = initiative.Phases.ToDictionary(p => p.Id, p => p.Name);

        return View(new BaselinesModel
        {
            Initiative = initiative,
            Baselines = baselines,
            Selected = selected,
            Previous = previous,
            LiveForecast = forecast,
            Lines = selected is null ? [] : BaselineLines(selected, previous, phaseNames, typeNames),
            Requests = initiative.RebaselineRequests.OrderByDescending(r => r.Id).ToList(),
            CanManage = InitiativeAccess.CanManage(currentUser, initiative),
            CanApprove = InitiativeAccess.CanApproveRebaseline(currentUser)
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
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Forecast baseline v{baseline.Version} captured ({baseline.TotalHours:N1} h, {baseline.TotalCost:C0}). Scope is locked.", id);
    }

    /// <summary>Administrator queue of pending re-baseline requests.</summary>
    [HttpGet("Rebaselines")]
    [Authorize(Policy = AppPolicies.Admin)]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var pending = await db.RebaselineRequests.Include(r => r.Initiative)
            .Where(r => r.Status == RebaselineStatus.Pending).AsNoTracking().ToListAsync(ct);
        return View(pending.OrderBy(r => r.Id).ToList());
    }

    // ----- Helpers -----

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct) =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations)
            .Include(i => i.Baselines)
            .Include(i => i.RebaselineRequests)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) => db.PublishedRateCardsAsync(ct);

    private static object BaselineDiff(ForecastBaseline b, int? requestId = null) =>
        new { b.Version, b.TotalHours, b.TotalCost, LineCount = b.Lines.Count, b.Reason, RequestId = requestId };

    private static List<BaselineLineRow> BaselineLines(
        ForecastBaseline selected, ForecastBaseline? previous,
        IReadOnlyDictionary<int, string> phaseNames, IReadOnlyDictionary<int, string> typeNames)
    {
        static string Key(ForecastBaselineLine l) => $"{l.PhaseId}|{l.ResourceTypeId}|{l.Seniority}|{l.Location}|{l.ResourcingClass}";
        var prev = (previous?.Lines ?? []).GroupBy(Key).ToDictionary(g => g.Key, g => (Hours: g.Sum(l => l.Hours), Cost: g.Sum(l => l.Cost)));
        var rows = selected.Lines.GroupBy(Key).Select(g =>
        {
            var first = g.First();
            prev.Remove(g.Key, out var p);
            return new BaselineLineRow(
                phaseNames.GetValueOrDefault(first.PhaseId, $"Phase #{first.PhaseId}"),
                typeNames.GetValueOrDefault(first.ResourceTypeId, $"Type #{first.ResourceTypeId}"),
                first.Seniority, first.Location, first.ResourcingClass,
                g.Sum(l => l.Hours), first.HourlyRate, g.Sum(l => l.Cost),
                previous is null ? null : g.Sum(l => l.Hours) - p.Hours,
                previous is null ? null : g.Sum(l => l.Cost) - p.Cost);
        }).ToList();

        // Lines that existed in the previous version but were removed.
        foreach (var (key, p) in prev)
        {
            var l = previous!.Lines.First(x => Key(x) == key);
            rows.Add(new BaselineLineRow(
                phaseNames.GetValueOrDefault(l.PhaseId, $"Phase #{l.PhaseId}"),
                typeNames.GetValueOrDefault(l.ResourceTypeId, $"Type #{l.ResourceTypeId}"),
                l.Seniority, l.Location, l.ResourcingClass, 0m, l.HourlyRate, 0m, -p.Hours, -p.Cost));
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
