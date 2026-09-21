using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Exports;
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
public class ScenariosController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, IEnumerable<IExportWriter> writers) : Controller
{
    private const string Entity = nameof(Initiative);

    [HttpGet("Initiatives/{id:int}/Scenarios")]
    public async Task<IActionResult> Compare(int id, CancellationToken ct)
    {
        var model = await BuildCompareAsync(id, printable: false, ct);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>Print-friendly scenario comparison (no app chrome, links or actions) plus each plan's phase schedule; the browser's Print → Save as PDF produces the PDF.</summary>
    [HttpGet("Initiatives/{id:int}/Scenarios/Print")]
    public async Task<IActionResult> Print(int id, CancellationToken ct)
    {
        var model = await BuildCompareAsync(id, printable: true, ct);
        return model is null ? NotFound() : View(model);
    }

    /// <summary>The compare table as CSV/XLSX (one value column per plan, a delta-vs-live column per scenario) plus each plan's phases.</summary>
    [HttpGet("Initiatives/{id:int}/Scenarios/Export")]
    public async Task<IActionResult> Export(int id, string format, CancellationToken ct)
    {
        var writer = writers.FirstOrDefault(w => string.Equals(w.Extension, format?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var model = await BuildCompareAsync(id, printable: false, ct);
        if (model is null)
        {
            return NotFound();
        }

        var bytes = writer.Write(ScenarioCompareExport.Build(model.Comparison, model.ResourceTypeNames));
        audit.Record(Entity, model.Parent.Id, AuditActions.Export, new { Format = writer.Extension, Scenarios = model.Comparison.Columns.Count - 1 });
        await db.SaveChangesAsync(ct);
        return File(bytes, writer.ContentType, $"scenarios-{model.Parent.Id}-{ExportFormats.SafeFileName(model.Parent.Name)}.{writer.Extension}");
    }

    private async Task<ScenarioCompareModel?> BuildCompareAsync(int id, bool printable, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return null;
        }

        var parentId = initiative.ScenarioOfId ?? initiative.Id;
        var parent = parentId == initiative.Id ? initiative : await LoadAsync(parentId, ct);
        if (parent is null)
        {
            return null;
        }

        var scenarios = await ScenarioQuery().Where(s => s.ScenarioOfId == parent.Id).ToListAsync(ct);
        var cards = await db.PricingRateCardsAsync(ct);
        return new ScenarioCompareModel
        {
            Parent = parent,
            Comparison = ScenarioComparison.Build(parent, scenarios, cards),
            ResourceTypeNames = await db.ResourceTypeNamesAsync(ct),
            CanEdit = InitiativeAccess.CanEdit(currentUser, parent),
            CanPromote = InitiativeAccess.CanManage(currentUser, parent) && InitiativeAccess.IsScopeEditable(parent),
            NewScenario = new NewScenarioModel { InitiativeId = parent.Id, Name = $"{parent.Name} — Scenario {(char)('A' + Math.Min(scenarios.Count, 25))}" },
            Printable = printable,
            GeneratedAt = clock.GetUtcNow()
        };
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
            .Include(i => i.Allocations).ThenInclude(a => a.ResourcingClass)
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
