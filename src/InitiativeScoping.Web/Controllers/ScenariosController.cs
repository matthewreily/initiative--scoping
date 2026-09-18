using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using InitiativeScoping.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>What-if scenarios: clone an initiative's plan, compare alternatives side by side, promote one back onto the initiative.</summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class ScenariosController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock) : Controller
{
    private const string Entity = nameof(Initiative);

    [HttpGet("Initiatives/{id:int}/Scenarios")]
    public async Task<IActionResult> Compare(int id, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        var parentId = initiative.ScenarioOfId ?? initiative.Id;
        var parent = parentId == initiative.Id ? initiative : await LoadAsync(parentId, ct);
        if (parent is null)
        {
            return NotFound();
        }

        var scenarios = await ScenarioQuery().Where(s => s.ScenarioOfId == parent.Id).ToListAsync(ct);
        var cards = await db.PricingRateCardsAsync(ct);
        return View(new ScenarioCompareModel
        {
            Parent = parent,
            Comparison = ScenarioComparison.Build(parent, scenarios, cards),
            ResourceTypeNames = await db.ResourceTypeNamesAsync(ct),
            CanEdit = InitiativeAccess.CanEdit(currentUser, parent),
            CanPromote = InitiativeAccess.CanManage(currentUser, parent) && InitiativeAccess.IsScopeEditable(parent),
            NewScenario = new NewScenarioModel { InitiativeId = parent.Id, Name = $"{parent.Name} — Scenario {(char)('A' + Math.Min(scenarios.Count, 25))}" }
        });
    }

    [HttpPost("Initiatives/{id:int}/Scenarios")]
    [Authorize(Policy = AppPolicies.CanEdit)]
    public async Task<IActionResult> Create(int id, NewScenarioModel model, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);
        if (source is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, source))
        {
            return Forbid();
        }

        var name = model.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return RedirectWithError("Give the scenario a name.", id);
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithError(ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid scenario.", id);
        }

        var scenario = ScenarioPlanner.Clone(source, name, currentUser.UserId, clock.GetUtcNow());
        db.Initiatives.Add(scenario);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, scenario.Id, AuditActions.Create, new { scenario.Name, Scenario = true, ScenarioOf = scenario.ScenarioOfId, ClonedFrom = source.Id });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Scenario '{scenario.Name}' created from '{source.Name}'. Edit its plan here, then compare and promote.";
        return RedirectToAction("Details", "Initiatives", new { id = scenario.Id });
    }

    [HttpPost("Initiatives/{id:int}/Scenarios/{scenarioId:int}/Promote")]
    [Authorize(Policy = AppPolicies.CanEdit)]
    public async Task<IActionResult> Promote(int id, int scenarioId, CancellationToken ct)
    {
        var parent = await LoadAsync(id, ct);
        var scenario = await LoadAsync(scenarioId, ct);
        if (parent is null || scenario is null || scenario.ScenarioOfId != parent.Id)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanManage(currentUser, parent))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(parent))
        {
            return RedirectWithError("Scope is locked; a scenario can only be promoted in Draft or during an approved re-baseline.", id);
        }

        if (scenario.Status != InitiativeStatus.Draft)
        {
            return RedirectWithError($"Scenario '{scenario.Name}' is {scenario.Status} and cannot be promoted.", id);
        }

        if (await HasOperationalDataAsync(scenario.Id, ct))
        {
            return RedirectWithError($"Scenario '{scenario.Name}' has actuals, source mappings or baselines attached; remove them before promoting.", id);
        }

        var cards = await db.PricingRateCardsAsync(ct);
        var before = ForecastCalculator.Calculate(parent, cards);
        var after = ForecastCalculator.Calculate(scenario, cards);

        db.InitiativeAllocations.RemoveRange(parent.Allocations);
        db.InitiativeNonLaborCosts.RemoveRange(parent.NonLaborCosts);
        db.Phases.RemoveRange(parent.Phases);
        db.InitiativeBusinessUnits.RemoveRange(parent.ParticipatingBusinessUnits);
        var plan = ScenarioPlanner.Promote(scenario, parent);
        db.InitiativeBusinessUnits.AddRange(parent.ParticipatingBusinessUnits);
        db.Phases.AddRange(plan.Phases);
        db.InitiativeAllocations.AddRange(plan.Allocations);
        db.InitiativeNonLaborCosts.AddRange(plan.NonLaborCosts);

        RemoveGraph(scenario);
        audit.Record(Entity, parent.Id, AuditActions.Update, new
        {
            Action = "PromoteScenario", Scenario = scenario.Name, ScenarioId = scenario.Id,
            HoursBefore = before.TotalHours, HoursAfter = after.TotalHours, CostBefore = before.TotalCost, CostAfter = after.TotalCost
        });
        audit.Record(Entity, scenario.Id, AuditActions.Delete, new { scenario.Name, Scenario = true, PromotedTo = parent.Id });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Scenario '{scenario.Name}' promoted: plan is now {after.TotalHours:N1} h / {after.TotalCost:C0} (was {before.TotalHours:N1} h / {before.TotalCost:C0}).";
        return RedirectToAction("Details", "Initiatives", new { id });
    }

    private async Task<bool> HasOperationalDataAsync(int initiativeId, CancellationToken ct) =>
        await db.ActualEntries.AnyAsync(e => e.InitiativeId == initiativeId, ct)
        || await db.ActualAdjustments.AnyAsync(a => a.InitiativeId == initiativeId, ct)
        || await db.InitiativeSourceMappings.AnyAsync(m => m.InitiativeId == initiativeId, ct)
        || await db.ForecastBaselines.AnyAsync(b => b.InitiativeId == initiativeId, ct);

    private void RemoveGraph(Initiative initiative)
    {
        db.InitiativeAllocations.RemoveRange(initiative.Allocations);
        db.InitiativeNonLaborCosts.RemoveRange(initiative.NonLaborCosts);
        db.Phases.RemoveRange(initiative.Phases);
        db.InitiativeBusinessUnits.RemoveRange(initiative.ParticipatingBusinessUnits);
        db.InitiativeMembers.RemoveRange(initiative.Members);
        db.Initiatives.Remove(initiative);
    }

    private IQueryable<Initiative> ScenarioQuery() =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.ParticipatingBusinessUnits)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations).ThenInclude(a => a.BusinessUnit)
            .Include(i => i.Allocations).ThenInclude(a => a.Vendor)
            .Include(i => i.Allocations).ThenInclude(a => a.People).ThenInclude(p => p.Person)
            .Include(i => i.Allocations).ThenInclude(a => a.Seniority)
            .Include(i => i.NonLaborCosts)
            .Include(i => i.RebaselineRequests)
            .AsSplitQuery();

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct) =>
        ScenarioQuery().FirstOrDefaultAsync(i => i.Id == id, ct);

    private IActionResult RedirectWithError(string message, int id)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Compare), new { id });
    }
}
