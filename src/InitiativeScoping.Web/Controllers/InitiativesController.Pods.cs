using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

public partial class InitiativesController
{
    public async Task<IActionResult> DevelopmentPods(int id, int? podId, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null) return NotFound();
        return View(await PodModel(initiative, podId ?? 0, ct));
    }

    [HttpPost]
    public async Task<IActionResult> UpdateApplications(int id, int[] applicationIds, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null) return NotFound();
        if (!InitiativeAccess.CanEdit(currentUser, initiative)) return Forbid();
        if (!InitiativeAccess.IsScopeEditable(initiative)) return RedirectWithError(ScopeLockedMessage, id);
        var ids = applicationIds.Distinct().ToArray();
        if (await db.ApplicationPlatforms.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) != ids.Length)
            return RedirectWithError("Choose active applications/platforms only.", id);
        var existing = await db.InitiativeApplications.Where(x => x.InitiativeId == id).ToListAsync(ct);
        var before = existing.Select(x => x.ApplicationPlatformId).ToArray();
        db.InitiativeApplications.RemoveRange(existing.Where(x => !ids.Contains(x.ApplicationPlatformId)));
        db.InitiativeApplications.AddRange(ids.Except(before).Select(appId => new InitiativeApplication { InitiativeId = id, ApplicationPlatformId = appId }));
        audit.Record(nameof(Initiative), id, AuditActions.Update, new { ApplicationsBefore = before, ApplicationsAfter = ids });
        await db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(DevelopmentPods), new { id });
    }

    [HttpPost]
    public async Task<IActionResult> AssignPod(int id, PodAssignmentModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null) return NotFound();
        if (!InitiativeAccess.CanEdit(currentUser, initiative)) return Forbid();
        if (!InitiativeAccess.IsScopeEditable(initiative)) return RedirectWithError(ScopeLockedMessage, id);
        var pod = await LoadPod(model.PodId, ct);
        var phases = initiative.Phases.Where(p => model.PhaseIds.Contains(p.Id)).ToList();
        if (!ModelState.IsValid || pod?.IsActive != true || phases.Count == 0 || phases.Count != model.PhaseIds.Distinct().Count() || model.AllocationPercent <= 0 || model.AllocationPercent > 100)
            return RedirectWithError("Choose an active pod, one or more initiative phases and allocation % greater than 0 and at most 100.", id);
        var calendar = await workCalendar.GetAsync(ct);
        var plan = PodPlanner.Assign(id, pod, phases, model.AllocationPercent, await LoadRateCardsAsync(ct), calendar.Holidays, calendar.HoursPerDay);
        var existing = initiative.Allocations.Where(a => a.PodId == pod.Id).ToList();
        var add = plan.Add.Where(a => !existing.Any(e => e.PhaseId == a.PhaseId && e.PersonIds.Intersect(a.PersonIds).Any())).ToList();
        db.InitiativeAllocations.AddRange(add);
        audit.Record(nameof(Initiative), id, AuditActions.Update, new { Operation = "AssignPod", PodId = pod.Id, model.PhaseIds, model.AllocationPercent, Added = add.Count, plan.Skipped });
        await db.SaveChangesAsync(ct);
        var effective = string.Join("; ", add.Select(a => $"{pod.Members.First(m => m.PersonId == a.PersonIds.Single()).Person!.DisplayName} ({phases.First(p => p.Id == a.PhaseId).Name}): {a.AllocationPercent:0.######}%"));
        return RedirectWithSuccess($"Pod assigned: {add.Count} seats added. Effective allocations: {effective}. {SkippedMessage(plan)}", id);
    }

    public async Task<IActionResult> ResyncPod(int id, int podId, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null) return NotFound();
        if (!InitiativeAccess.CanEdit(currentUser, initiative)) return Forbid();
        if (!InitiativeAccess.IsScopeEditable(initiative)) return RedirectWithError(ScopeLockedMessage, id);
        var pod = await LoadPod(podId, ct);
        if (pod?.IsActive != true) return RedirectWithError("Choose an active pod.", id);
        var calendar = await workCalendar.GetAsync(ct);
        var plan = PodPlanner.Resync(id, pod, initiative.Phases, initiative.Allocations, await LoadRateCardsAsync(ct), calendar.Holidays, calendar.HoursPerDay);
        return View(new InitiativePodsModel { Initiative = initiative, PodId = podId, Pod = pod, Preview = plan });
    }

    [HttpPost]
    public async Task<IActionResult> ConfirmResyncPod(int id, int podId, int[] updateAllocationIds, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null) return NotFound();
        if (!InitiativeAccess.CanEdit(currentUser, initiative)) return Forbid();
        if (!InitiativeAccess.IsScopeEditable(initiative)) return RedirectWithError(ScopeLockedMessage, id);
        var pod = await LoadPod(podId, ct);
        if (pod?.IsActive != true) return RedirectWithError("Choose an active pod.", id);
        var calendar = await workCalendar.GetAsync(ct);
        var plan = PodPlanner.Resync(id, pod, initiative.Phases, initiative.Allocations, await LoadRateCardsAsync(ct), calendar.Holidays, calendar.HoursPerDay);
        db.InitiativeAllocations.RemoveRange(plan.Remove);
        foreach (var update in plan.Update)
        {
            db.InitiativeAllocationPeople.RemoveRange(update.People);
            update.Allocation.Quantity -= update.People.Count;
        }
        db.InitiativeAllocations.AddRange(plan.Add);
        var selected = plan.SeatsToUpdate.Where(u => updateAllocationIds.Contains(u.Allocation.Id)).ToList();
        var changes = selected.Select(u => new { u.Allocation.Id, BeforePercent = u.Allocation.AllocationPercent, BeforeHours = u.Allocation.EstimatedHours, u.AllocationPercent, u.EstimatedHours, u.PodPercent, u.MembershipPercent, u.HandEdited }).ToArray();
        foreach (var change in selected) change.Apply();
        audit.Record(nameof(Initiative), id, AuditActions.Update, new { Operation = "ResyncPod", PodId = podId, Added = plan.Add.Select(a => new { a.PhaseId, PersonIds = a.PersonIds.ToArray(), a.AllocationPercent }).ToArray(), Removed = plan.Remove.Select(a => a.Id).ToArray(), Updated = plan.Update.Select(u => new { u.Allocation.Id, RemovedPersonIds = u.People.Select(p => p.PersonId).ToArray(), u.Allocation.Quantity }).ToArray(), MembershipUpdates = changes, plan.Skipped });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Pod re-synced: {plan.Add.Count} seats added, {plan.Remove.Count} allocations removed, {plan.Update.Sum(u => u.People.Count)} departed people removed from retained allocations, {selected.Count} seats updated. {SkippedMessage(plan)}", id);
    }

    private Task<Pod?> LoadPod(int id, CancellationToken ct) => db.Pods.Include(p => p.Members).ThenInclude(m => m.Person).ThenInclude(p => p!.ResourcingClass).AsSplitQuery().SingleOrDefaultAsync(p => p.Id == id, ct);
    private async Task<InitiativePodsModel> PodModel(Initiative initiative, int podId, CancellationToken ct)
    {
        var ids = await db.InitiativeApplications.Where(x => x.InitiativeId == initiative.Id).Select(x => x.ApplicationPlatformId).ToArrayAsync(ct);
        var pods = await db.Pods.Where(x => x.IsActive).Include(x => x.Applications).Include(x => x.Members).ThenInclude(m => m.Person).OrderBy(x => x.Name).AsSplitQuery().ToListAsync(ct);
        return new InitiativePodsModel
        {
            Initiative = initiative, CanEdit = InitiativeAccess.CanEdit(currentUser, initiative) && InitiativeAccess.IsScopeEditable(initiative),
            Applications = await db.ApplicationPlatforms.Where(x => x.IsActive || ids.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync(ct),
            ApplicationIds = ids, Pods = pods, Suggested = pods.Where(p => p.Applications.Any(a => ids.Contains(a.ApplicationPlatformId))).ToList(), PodId = podId
        };
    }
    private static string SkippedMessage(PodPlan plan) => plan.Skipped.Count == 0 ? "" : "Skipped: " + string.Join("; ", plan.Skipped);
}
