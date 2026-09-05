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
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class InitiativesController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, IConfiguration config, IWorkCalendar workCalendar) : Controller
{
    private const string Entity = nameof(Initiative);
    private const string ScopeLockedMessage = "Scope is locked; it can only change in Draft or during an approved re-baseline.";

    // ----- List / create / edit -----

    public async Task<IActionResult> Index(InitiativeStatus? status, int? businessUnitId, string? search, CancellationToken ct)
    {
        var query = db.Initiatives.Include(i => i.BusinessUnit).Include(i => i.Phases).Include(i => i.Allocations).Include(i => i.NonLaborCosts).AsSplitQuery().AsQueryable();
        if (status is not null)
        {
            query = query.Where(i => i.Status == status);
        }

        if (businessUnitId is not null)
        {
            query = query.Where(i => i.BusinessUnitId == businessUnitId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(i => i.Name.ToLower().Contains(term));
        }

        var initiatives = await query.OrderByDescending(i => i.Id).ToListAsync(ct);
        var cards = await LoadRateCardsAsync(ct);
        var items = initiatives.Select(i =>
        {
            var forecast = ForecastCalculator.Calculate(i, cards);
            return new InitiativeListItem
            {
                Initiative = i, PhaseCount = i.Phases.Count,
                TotalHours = forecast.TotalHours, TotalCost = forecast.TotalCost, IsComplete = forecast.IsComplete
            };
        }).ToList();

        return View(new InitiativeIndexModel
        {
            Items = items, Status = status, BusinessUnitId = businessUnitId, Search = search,
            BusinessUnits = await BusinessUnitListAsync(ct, includeInactive: true),
            CanCreate = InitiativeAccess.CanCreate(currentUser)
        });
    }

    [Authorize(Policy = AppPolicies.CanEditInitiatives)]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateEditLists(ct);
        return View("Edit", new InitiativeEditModel());
    }

    [HttpPost, Authorize(Policy = AppPolicies.CanEditInitiatives)]
    public async Task<IActionResult> Create(InitiativeEditModel model, CancellationToken ct)
    {
        await ValidateInitiative(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateEditLists(ct);
            return View("Edit", model);
        }

        var initiative = new Initiative
        {
            Name = model.Name.Trim(),
            Description = model.Description?.Trim(),
            BusinessUnitId = model.BusinessUnitId,
            SponsoringTeam = model.SponsoringTeam?.Trim(),
            SizingMethod = model.SizingMethod,
            SizeKey = model.SizingMethod == SizingMethod.Direct ? null : model.SizeKey?.Trim(),
            PlanningMode = model.PlanningMode,
            TargetStart = model.TargetStart,
            TargetEnd = model.PlanningMode == PlanningMode.FixedDuration ? model.TargetEnd : null,
            VarianceThresholdPct = model.VarianceThresholdPct,
            CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow()
        };
        initiative.Members.Add(new InitiativeMember { UserId = currentUser.UserId, Role = InitiativeMemberRole.Owner });
        db.Initiatives.Add(initiative);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, initiative.Id, AuditActions.Create, new { initiative.Name, initiative.BusinessUnitId, initiative.SizingMethod, initiative.SizeKey, initiative.PlanningMode, initiative.TargetStart, initiative.TargetEnd });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Initiative '{initiative.Name}' created. Add phases and allocations to build the forecast.", initiative.Id);
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        await PopulateEditLists(ct);
        return View(new InitiativeEditModel
        {
            Id = initiative.Id, Name = initiative.Name, Description = initiative.Description, BusinessUnitId = initiative.BusinessUnitId,
            SponsoringTeam = initiative.SponsoringTeam, SizingMethod = initiative.SizingMethod, SizeKey = initiative.SizeKey,
            PlanningMode = initiative.PlanningMode, TargetStart = initiative.TargetStart, TargetEnd = initiative.TargetEnd,
            VarianceThresholdPct = initiative.VarianceThresholdPct
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, InitiativeEditModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        model.Id = id;
        await ValidateInitiative(model, ct);
        var scheduleChanged = initiative.PlanningMode != model.PlanningMode || initiative.TargetStart != model.TargetStart || initiative.TargetEnd != model.TargetEnd;
        if (scheduleChanged && !InitiativeAccess.IsScopeEditable(initiative))
        {
            ModelState.AddModelError(nameof(model.TargetStart), ScopeLockedMessage);
        }

        if (model.PlanningMode == PlanningMode.FixedDuration && model.TargetEnd is not null && initiative.Phases.Count > 0)
        {
            var tiling = DurationCalculator.ValidateTiling(model.TargetStart, model.TargetEnd.Value, initiative.Phases);
            if (tiling is not null)
            {
                ModelState.AddModelError(nameof(model.TargetEnd), $"Existing phases must tile the new window: {tiling} Adjust the phases first.");
            }
        }

        if (!ModelState.IsValid)
        {
            await PopulateEditLists(ct);
            return View(model);
        }

        var before = Snapshot(initiative);
        initiative.Name = model.Name.Trim();
        initiative.Description = model.Description?.Trim();
        initiative.BusinessUnitId = model.BusinessUnitId;
        initiative.SponsoringTeam = model.SponsoringTeam?.Trim();
        initiative.SizingMethod = model.SizingMethod;
        initiative.SizeKey = model.SizingMethod == SizingMethod.Direct ? null : model.SizeKey?.Trim();
        initiative.PlanningMode = model.PlanningMode;
        initiative.TargetStart = model.TargetStart;
        initiative.TargetEnd = model.PlanningMode == PlanningMode.FixedDuration ? model.TargetEnd : null;
        initiative.VarianceThresholdPct = model.VarianceThresholdPct;
        var recomputed = 0;
        if (scheduleChanged && initiative.PlanningMode == PlanningMode.FixedDuration)
        {
            var calendar = await workCalendar.GetAsync(ct);
            // Allocations created in effort-driven mode keep their hours: derive the staffing % that reproduces them.
            foreach (var allocation in initiative.Allocations.Where(a => a.AllocationPercent is null))
            {
                var phase = initiative.Phases.First(p => p.Id == allocation.PhaseId);
                allocation.AllocationPercent = DurationCalculator.PercentFor(
                    allocation.EstimatedHours,
                    DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, calendar.Holidays), calendar.HoursPerDay);
            }

            recomputed = RecomputeFixedDurationHours(initiative, initiative.Allocations, calendar);
        }
        else if (scheduleChanged && initiative.PlanningMode == PlanningMode.EffortDriven)
        {
            foreach (var allocation in initiative.Allocations)
            {
                allocation.AllocationPercent = null;
            }
        }

        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Before = before, After = Snapshot(initiative), RecomputedAllocations = recomputed });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess(recomputed > 0 ? $"Initiative updated; {recomputed} allocation hour value(s) recomputed from the fixed-duration schedule." : "Initiative updated.", id);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
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
            return RedirectWithError("Only Draft initiatives can be deleted; cancel it instead.", id);
        }

        db.InitiativeAllocations.RemoveRange(initiative.Allocations);
        db.Phases.RemoveRange(initiative.Phases);
        db.Initiatives.Remove(initiative);
        audit.Record(Entity, initiative.Id, AuditActions.Delete, new { initiative.Name });
        await db.SaveChangesAsync(ct);
        TempData["Success"] = $"Initiative '{initiative.Name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ----- Details -----

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct, includeHistory: true);
        if (initiative is null)
        {
            return NotFound();
        }

        var cards = await LoadRateCardsAsync(ct);
        var forecast = ForecastCalculator.Calculate(initiative, cards);
        var typeNames = await db.ResourceTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var phaseNames = initiative.Phases.ToDictionary(p => p.Id, p => p.Name);
        var orderedPhases = initiative.Phases.OrderBy(p => p.Sequence).ThenBy(p => p.PlannedStart).ToList();
        var actuals = await db.LoadActualsAsync(initiative, config.GetValue<decimal?>(ActualsQueries.DefaultThresholdKey), ct);
        var unmappedForProjects = await db.ActualEntries.CountAsync(e => e.InitiativeId == id && e.IsUnmapped, ct);
        var fixedDuration = initiative.PlanningMode == PlanningMode.FixedDuration && initiative.TargetEnd is not null
            ? BuildFixedDurationSummary(initiative, orderedPhases, forecast, await workCalendar.GetAsync(ct))
            : null;
        var nextPhaseStart = orderedPhases.LastOrDefault()?.PlannedEnd.AddDays(1) ?? initiative.TargetStart;
        var nextPhaseEnd = initiative.PlanningMode == PlanningMode.FixedDuration && initiative.TargetEnd is not null && initiative.TargetEnd.Value >= nextPhaseStart
            ? initiative.TargetEnd.Value
            : nextPhaseStart.AddDays(29);

        var model = new InitiativeDetailsModel
        {
            Initiative = initiative,
            Forecast = forecast,
            ByPhase = Rollup(forecast, l => phaseNames.GetValueOrDefault(l.Allocation.PhaseId, "?"),
                orderedPhases.Select(p => p.Name)),
            ByResourceType = Rollup(forecast, l => typeNames.GetValueOrDefault(l.Allocation.ResourceTypeId, "?")),
            ByClass = Rollup(forecast, l => l.Allocation.ResourcingClass == ResourcingClass.InternalFte ? "Internal FTE" : "Vendor"),
            Gantt = BuildGantt(orderedPhases),
            FixedDuration = fixedDuration,
            ResourceTypeNames = typeNames,
            NewPhase = new PhaseEditModel
            {
                InitiativeId = id,
                PlannedStart = nextPhaseStart,
                PlannedEnd = nextPhaseEnd
            },
            NewAllocation = new AllocationEditModel { InitiativeId = id },
            NewNonLaborCost = new NonLaborCostEditModel { InitiativeId = id },
            CatalogOptions = await CatalogOptionsAsync(ct),
            CostPreviewWindows = CostPreviewWindows(initiative),
            NewMember = new MemberEditModel { InitiativeId = id },
            ApplySize = new ApplySizeModel
            {
                InitiativeId = id,
                Method = initiative.SizingMethod == SizingMethod.Direct ? SizingMethod.TShirt : initiative.SizingMethod,
                SizeKey = initiative.SizeKey ?? string.Empty
            },
            Phases = new SelectList(orderedPhases, "Id", "Name"),
            ResourceTypes = new SelectList(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name"),
            SizeOptions = await SizeOptionsAsync(ct),
            CanEdit = InitiativeAccess.CanEdit(currentUser, initiative),
            CanManage = InitiativeAccess.CanManage(currentUser, initiative),
            ScopeEditable = InitiativeAccess.IsScopeEditable(initiative),
            CanApproveRebaseline = InitiativeAccess.CanApproveRebaseline(currentUser),
            ActivationBlockers = initiative.Status == InitiativeStatus.Draft ? InitiativeLifecycle.BaselineBlockers(initiative, forecast) : [],
            StatusTransitions = InitiativeLifecycle.AllowedTransitions(initiative.Status)
                .Where(s => !(initiative.Status == InitiativeStatus.Draft && s == InitiativeStatus.Active)).ToList(),
            Variance = actuals.Variance,
            UnmappedForMappedProjects = unmappedForProjects
        };
        return View(model);
    }

    // ----- Phases -----

    [HttpPost]
    public async Task<IActionResult> AddPhase(int id, PhaseEditModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, id);
        }

        ValidatePhase(model, initiative);
        var phase = new Phase
        {
            Name = model.Name.Trim(),
            Sequence = (initiative.Phases.Count == 0 ? 0 : initiative.Phases.Max(p => p.Sequence)) + 1,
            PlannedStart = model.PlannedStart,
            PlannedEnd = model.PlannedEnd
        };
        ValidateFixedDurationTiling(initiative, initiative.Phases.Append(phase));
        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        initiative.Phases.Add(phase);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Phase), phase.Id, AuditActions.Create, new { initiative.Id, phase.Name, phase.PlannedStart, phase.PlannedEnd });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Phase '{phase.Name}' added.", id);
    }

    public async Task<IActionResult> EditPhase(int id, CancellationToken ct)
    {
        var phase = await db.Phases.Include(p => p.Initiative!).ThenInclude(i => i.Members).Include(p => p.Initiative!).ThenInclude(i => i.RebaselineRequests).Include(p => p.DateHistory).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (phase is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, phase.Initiative!))
        {
            return Forbid();
        }

        ViewBag.Initiative = phase.Initiative;
        ViewBag.History = phase.DateHistory.OrderByDescending(h => h.ChangedAt).ToList();
        return View(new PhaseEditModel
        {
            Id = phase.Id, InitiativeId = phase.InitiativeId, Name = phase.Name, PlannedStart = phase.PlannedStart, PlannedEnd = phase.PlannedEnd
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditPhase(int id, PhaseEditModel model, CancellationToken ct)
    {
        var phase = await db.Phases.Include(p => p.Initiative!).ThenInclude(i => i.Members).Include(p => p.Initiative!).ThenInclude(i => i.RebaselineRequests).Include(p => p.Initiative!).ThenInclude(i => i.Phases)
            .Include(p => p.DateHistory).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (phase is null)
        {
            return NotFound();
        }

        var initiative = phase.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        model.Id = id;
        model.InitiativeId = initiative.Id;
        ValidatePhase(model, initiative);
        ValidateFixedDurationTiling(initiative, initiative.Phases.Select(p => p.Id == id
            ? new Phase { Name = model.Name.Trim(), Sequence = p.Sequence, PlannedStart = model.PlannedStart, PlannedEnd = model.PlannedEnd }
            : p));
        if (!ModelState.IsValid)
        {
            ViewBag.Initiative = initiative;
            ViewBag.History = phase.DateHistory.OrderByDescending(h => h.ChangedAt).ToList();
            return View(model);
        }

        var datesChanged = phase.PlannedStart != model.PlannedStart || phase.PlannedEnd != model.PlannedEnd;
        if (datesChanged)
        {
            phase.DateHistory.Add(new PhaseDateHistory
            {
                OldStart = phase.PlannedStart, OldEnd = phase.PlannedEnd,
                NewStart = model.PlannedStart, NewEnd = model.PlannedEnd,
                ChangedBy = currentUser.UserId, ChangedAt = clock.GetUtcNow(), Reason = model.Reason?.Trim()
            });
        }

        var before = new { phase.Name, phase.PlannedStart, phase.PlannedEnd };
        phase.Name = model.Name.Trim();
        phase.PlannedStart = model.PlannedStart;
        phase.PlannedEnd = model.PlannedEnd;
        var recomputed = 0;
        if (datesChanged && initiative.PlanningMode == PlanningMode.FixedDuration)
        {
            var allocations = await db.InitiativeAllocations.Where(a => a.PhaseId == id).ToListAsync(ct);
            recomputed = RecomputeFixedDurationHours(initiative, allocations, await workCalendar.GetAsync(ct));
        }

        audit.Record(nameof(Phase), phase.Id, AuditActions.Update, new { initiative.Id, Before = before, After = new { phase.Name, phase.PlannedStart, phase.PlannedEnd }, model.Reason, RecomputedAllocations = recomputed });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess(recomputed > 0 ? $"Phase '{phase.Name}' updated; {recomputed} allocation hour value(s) recomputed." : $"Phase '{phase.Name}' updated.", initiative.Id);
    }

    [HttpPost]
    public async Task<IActionResult> DeletePhase(int id, CancellationToken ct)
    {
        var phase = await db.Phases.Include(p => p.Initiative!).ThenInclude(i => i.Members).Include(p => p.Initiative!).ThenInclude(i => i.RebaselineRequests).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (phase is null)
        {
            return NotFound();
        }

        var initiative = phase.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        if (await db.InitiativeAllocations.AnyAsync(a => a.PhaseId == id, ct))
        {
            return RedirectWithError($"Phase '{phase.Name}' has allocations; remove or move them first.", initiative.Id);
        }

        db.Phases.Remove(phase);
        audit.Record(nameof(Phase), phase.Id, AuditActions.Delete, new { initiative.Id, phase.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Phase '{phase.Name}' deleted.", initiative.Id);
    }

    // ----- Allocations -----

    [HttpPost]
    public async Task<IActionResult> AddAllocation(int id, AllocationEditModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, id);
        }

        await ValidateAllocation(model, initiative, ct);
        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        var allocation = new InitiativeAllocation
        {
            PhaseId = model.PhaseId, ResourceTypeId = model.ResourceTypeId, Seniority = model.Seniority,
            Location = model.Location.Trim(), ResourcingClass = model.ResourcingClass, Quantity = model.Quantity,
            EstimatedHours = model.EstimatedHours, ContractReference = model.ContractReference?.Trim(), CostCenter = model.CostCenter?.Trim()
        };
        await ApplyAllocationEffortAsync(initiative, allocation, model, ct);
        initiative.Allocations.Add(allocation);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(InitiativeAllocation), allocation.Id, AuditActions.Create, AllocationSnapshot(allocation));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Allocation added.", id);
    }

    public async Task<IActionResult> EditAllocation(int id, CancellationToken ct)
    {
        var allocation = await db.InitiativeAllocations.Include(a => a.Initiative!).ThenInclude(i => i.Members).Include(a => a.Initiative!).ThenInclude(i => i.RebaselineRequests)
            .Include(a => a.Initiative!).ThenInclude(i => i.Phases).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (allocation is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, allocation.Initiative!))
        {
            return Forbid();
        }

        await PopulateAllocationLists(allocation.Initiative!, ct);
        return View(new AllocationEditModel
        {
            Id = allocation.Id, InitiativeId = allocation.InitiativeId, PhaseId = allocation.PhaseId, ResourceTypeId = allocation.ResourceTypeId,
            Seniority = allocation.Seniority, Location = allocation.Location, ResourcingClass = allocation.ResourcingClass,
            Quantity = allocation.Quantity, EstimatedHours = allocation.EstimatedHours, AllocationPercent = allocation.AllocationPercent,
            ContractReference = allocation.ContractReference, CostCenter = allocation.CostCenter
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditAllocation(int id, AllocationEditModel model, CancellationToken ct)
    {
        var allocation = await db.InitiativeAllocations.Include(a => a.Initiative!).ThenInclude(i => i.Members).Include(a => a.Initiative!).ThenInclude(i => i.RebaselineRequests)
            .Include(a => a.Initiative!).ThenInclude(i => i.Phases).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (allocation is null)
        {
            return NotFound();
        }

        var initiative = allocation.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        model.Id = id;
        model.InitiativeId = initiative.Id;
        await ValidateAllocation(model, initiative, ct);
        if (!ModelState.IsValid)
        {
            await PopulateAllocationLists(initiative, ct);
            return View(model);
        }

        var before = AllocationSnapshot(allocation);
        allocation.PhaseId = model.PhaseId;
        allocation.ResourceTypeId = model.ResourceTypeId;
        allocation.Seniority = model.Seniority;
        allocation.Location = model.Location.Trim();
        allocation.ResourcingClass = model.ResourcingClass;
        allocation.Quantity = model.Quantity;
        allocation.EstimatedHours = model.EstimatedHours;
        await ApplyAllocationEffortAsync(initiative, allocation, model, ct);
        allocation.ContractReference = model.ContractReference?.Trim();
        allocation.CostCenter = model.CostCenter?.Trim();
        audit.Record(nameof(InitiativeAllocation), allocation.Id, AuditActions.Update, new { Before = before, After = AllocationSnapshot(allocation) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Allocation updated.", initiative.Id);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteAllocation(int id, CancellationToken ct)
    {
        var allocation = await db.InitiativeAllocations.Include(a => a.Initiative!).ThenInclude(i => i.Members).Include(a => a.Initiative!).ThenInclude(i => i.RebaselineRequests).FirstOrDefaultAsync(a => a.Id == id, ct);
        if (allocation is null)
        {
            return NotFound();
        }

        var initiative = allocation.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        db.InitiativeAllocations.Remove(allocation);
        audit.Record(nameof(InitiativeAllocation), allocation.Id, AuditActions.Delete, AllocationSnapshot(allocation));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Allocation removed.", initiative.Id);
    }

    // ----- Non-labor costs -----

    [HttpPost]
    public async Task<IActionResult> AddNonLaborCost(int id, NonLaborCostEditModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, id);
        }

        await ValidateNonLaborCost(model, initiative, ct);
        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        var line = new InitiativeNonLaborCost { Description = model.Description.Trim() };
        ApplyNonLaborCost(line, model);
        initiative.NonLaborCosts.Add(line);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(InitiativeNonLaborCost), line.Id, AuditActions.Create, NonLaborSnapshot(line));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Non-labor cost added.", id);
    }

    public async Task<IActionResult> EditNonLaborCost(int id, CancellationToken ct)
    {
        var line = await LoadNonLaborCostAsync(id, ct);
        if (line is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, line.Initiative!))
        {
            return Forbid();
        }

        await PopulateNonLaborLists(line.Initiative!, ct);
        return View(new NonLaborCostEditModel
        {
            Id = line.Id, InitiativeId = line.InitiativeId, PhaseId = line.PhaseId, CostCatalogItemId = line.CostCatalogItemId,
            Category = line.Category, Description = line.Description, BillingModel = line.BillingModel, Quantity = line.Quantity,
            UnitCost = line.UnitCost, StartDate = line.StartDate, EndDate = line.EndDate,
            ContractReference = line.ContractReference, CostCenter = line.CostCenter
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditNonLaborCost(int id, NonLaborCostEditModel model, CancellationToken ct)
    {
        var line = await LoadNonLaborCostAsync(id, ct);
        if (line is null)
        {
            return NotFound();
        }

        var initiative = line.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        model.Id = id;
        model.InitiativeId = initiative.Id;
        await ValidateNonLaborCost(model, initiative, ct);
        if (!ModelState.IsValid)
        {
            await PopulateNonLaborLists(initiative, ct);
            return View(model);
        }

        var before = NonLaborSnapshot(line);
        ApplyNonLaborCost(line, model);
        audit.Record(nameof(InitiativeNonLaborCost), line.Id, AuditActions.Update, new { Before = before, After = NonLaborSnapshot(line) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Non-labor cost updated.", initiative.Id);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteNonLaborCost(int id, CancellationToken ct)
    {
        var line = await LoadNonLaborCostAsync(id, ct);
        if (line is null)
        {
            return NotFound();
        }

        var initiative = line.Initiative!;
        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, initiative.Id);
        }

        db.InitiativeNonLaborCosts.Remove(line);
        audit.Record(nameof(InitiativeNonLaborCost), line.Id, AuditActions.Delete, NonLaborSnapshot(line));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Non-labor cost removed.", initiative.Id);
    }

    // ----- Relative sizing -----

    [HttpPost]
    public async Task<IActionResult> ApplySize(int id, ApplySizeModel model, CancellationToken ct)
    {
        var initiative = await LoadAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanEdit(currentUser, initiative))
        {
            return Forbid();
        }

        if (!InitiativeAccess.IsScopeEditable(initiative))
        {
            return RedirectWithError(ScopeLockedMessage, id);
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        var key = model.SizeKey.Trim();
        var conversion = await db.SizingConversions.FirstOrDefaultAsync(c => c.Method == model.Method && c.Key == key, ct);
        if (conversion is null)
        {
            return RedirectWithError($"No sizing conversion for {model.Method} '{key}'. Ask an administrator to add one.", id);
        }

        var template = await db.AllocationTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Method == model.Method && t.SizeKey == key, ct);
        if (template is null || template.Lines.Count == 0)
        {
            return RedirectWithError($"No allocation template for {model.Method} '{key}' ({conversion.Hours}h). Add allocations manually or ask an administrator to add a template.", id);
        }

        var lines = template.Lines.OrderBy(l => l.Id).ToList();
        if (model.Replace)
        {
            db.InitiativeAllocations.RemoveRange(initiative.Allocations);
            initiative.Allocations.Clear();
        }

        var location = model.Location.Trim();
        var sizedLines = SizingApplier.Apply(conversion.Hours, lines);
        int createdPhases;
        string modeNote;
        if (initiative.PlanningMode == PlanningMode.FixedDuration)
        {
            if (initiative.TargetEnd is null)
            {
                return RedirectWithError("Set a target end date before applying a size to a fixed-duration initiative.", id);
            }

            var calendar = await workCalendar.GetAsync(ct);
            var phaseNames = SizingApplier.PhaseNames(lines);
            var phasesByName = initiative.Phases.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            createdPhases = 0;
            if (model.Replace)
            {
                // Re-tile the fixed window across the template phases, weighted by each phase's share of the template hours.
                // Existing phases with a matching name are re-dated (history recorded); others are removed (their allocations were just cleared).
                var weights = phaseNames.Select(n => lines.Where(l => string.Equals(l.PhaseName, n, StringComparison.OrdinalIgnoreCase)).Sum(l => l.Percent)).ToList();
                var windows = DurationCalculator.SplitWindow(initiative.TargetStart, initiative.TargetEnd.Value, weights);
                var obsolete = initiative.Phases.Where(p => !phaseNames.Contains(p.Name, StringComparer.OrdinalIgnoreCase)).ToList();
                db.Phases.RemoveRange(obsolete);
                foreach (var p in obsolete)
                {
                    initiative.Phases.Remove(p);
                }

                for (var i = 0; i < phaseNames.Count; i++)
                {
                    if (phasesByName.TryGetValue(phaseNames[i], out var existing))
                    {
                        existing.Sequence = i + 1;
                        if (existing.PlannedStart != windows[i].Start || existing.PlannedEnd != windows[i].End)
                        {
                            existing.DateHistory.Add(new PhaseDateHistory
                            {
                                OldStart = existing.PlannedStart, OldEnd = existing.PlannedEnd, NewStart = windows[i].Start, NewEnd = windows[i].End,
                                ChangedBy = currentUser.UserId, ChangedAt = clock.GetUtcNow(), Reason = $"Apply size {model.Method} '{key}' (fixed duration)"
                            });
                            existing.PlannedStart = windows[i].Start;
                            existing.PlannedEnd = windows[i].End;
                        }

                        continue;
                    }

                    var phase = new Phase { Name = phaseNames[i], Sequence = i + 1, PlannedStart = windows[i].Start, PlannedEnd = windows[i].End };
                    initiative.Phases.Add(phase);
                    phasesByName[phase.Name] = phase;
                    createdPhases++;
                }
            }
            else if (phaseNames.Any(n => !phasesByName.ContainsKey(n)))
            {
                return RedirectWithError("Template phases don't match the existing phases. Tick 'Replace existing allocations' to re-tile the schedule from the template, or rename phases to match.", id);
            }

            var zeroDayPhases = new List<string>();
            foreach (var sized in sizedLines)
            {
                var phase = phasesByName[sized.PhaseName];
                var workingDays = DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, calendar.Holidays);
                var percent = DurationCalculator.PercentFor(sized.Hours, workingDays, calendar.HoursPerDay);
                if (workingDays == 0)
                {
                    zeroDayPhases.Add(phase.Name);
                }

                initiative.Allocations.Add(new InitiativeAllocation
                {
                    Phase = phase, ResourceTypeId = sized.ResourceTypeId, Seniority = sized.Seniority,
                    Location = location, ResourcingClass = model.ResourcingClass, Quantity = 1,
                    AllocationPercent = percent,
                    EstimatedHours = DurationCalculator.Hours(percent, workingDays, calendar.HoursPerDay)
                });
            }

            var totalWorkingDays = DurationCalculator.WorkingDays(initiative.TargetStart, initiative.TargetEnd.Value, calendar.Holidays);
            modeNote = $" Staffing % set so the {totalWorkingDays} working days from {initiative.TargetStart:yyyy-MM-dd} to {initiative.TargetEnd:yyyy-MM-dd} deliver ~{conversion.Hours}h.";
            if (zeroDayPhases.Count > 0)
            {
                modeNote += $" Warning: phase(s) {string.Join(", ", zeroDayPhases.Distinct())} have no working days, so their allocations are 0%.";
            }
        }
        else
        {
            var phasesByName = initiative.Phases.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
            var nextSequence = (initiative.Phases.Count == 0 ? 0 : initiative.Phases.Max(p => p.Sequence)) + 1;
            var nextStart = initiative.Phases.Count == 0 ? initiative.TargetStart : initiative.Phases.Max(p => p.PlannedEnd).AddDays(1);
            createdPhases = 0;
            foreach (var name in SizingApplier.PhaseNames(lines))
            {
                if (phasesByName.ContainsKey(name))
                {
                    continue;
                }

                var phase = new Phase { Name = name, Sequence = nextSequence++, PlannedStart = nextStart, PlannedEnd = nextStart.AddDays(29) };
                nextStart = phase.PlannedEnd.AddDays(1);
                initiative.Phases.Add(phase);
                phasesByName[name] = phase;
                createdPhases++;
            }

            foreach (var sized in sizedLines)
            {
                initiative.Allocations.Add(new InitiativeAllocation
                {
                    Phase = phasesByName[sized.PhaseName], ResourceTypeId = sized.ResourceTypeId, Seniority = sized.Seniority,
                    Location = location, ResourcingClass = model.ResourcingClass, Quantity = 1, EstimatedHours = sized.Hours
                });
            }

            modeNote = string.Empty;
        }

        initiative.SizingMethod = model.Method;
        initiative.SizeKey = key;
        audit.Record(Entity, initiative.Id, AuditActions.Update, new
        {
            Action = "ApplySize", model.Method, SizeKey = key, conversion.Hours, TemplateId = template.Id, model.Replace, PhasesCreated = createdPhases, Lines = lines.Count, initiative.PlanningMode
        });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Applied {model.Method} '{key}' ({conversion.Hours}h) using template '{template.Name}': {lines.Count} allocation(s), {createdPhases} new phase(s).{modeNote} Adjust as needed.", id);
    }

    // ----- Members -----

    [HttpPost]
    public async Task<IActionResult> AddMember(int id, MemberEditModel model, CancellationToken ct)
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

        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        var userId = model.UserId.Trim();
        var existing = initiative.Members.FirstOrDefault(m => m.UserId == userId);
        if (existing is null)
        {
            initiative.Members.Add(new InitiativeMember { UserId = userId, Role = model.Role });
        }
        else
        {
            existing.Role = model.Role;
        }

        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Action = "AddMember", UserId = userId, model.Role });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"{userId} is now {model.Role}.", id);
    }

    [HttpPost]
    public async Task<IActionResult> RemoveMember(int id, string userId, CancellationToken ct)
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

        var member = initiative.Members.FirstOrDefault(m => m.UserId == userId);
        if (member is null)
        {
            return NotFound();
        }

        if (member.Role == InitiativeMemberRole.Owner && initiative.Members.Count(m => m.Role == InitiativeMemberRole.Owner) == 1)
        {
            return RedirectWithError("An initiative must keep at least one Owner.", id);
        }

        initiative.Members.Remove(member);
        db.InitiativeMembers.Remove(member);
        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Action = "RemoveMember", UserId = userId });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"{userId} removed.", id);
    }

    // ----- Source mappings -----

    [HttpPost]
    public async Task<IActionResult> AddSourceMapping(int id, string source, string externalProjectId, CancellationToken ct)
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

        if (!ActualsSources.All.Contains(source) || string.IsNullOrWhiteSpace(externalProjectId))
        {
            return RedirectWithError("Choose a source and enter the external project id.", id);
        }

        externalProjectId = externalProjectId.Trim();
        var taken = await db.InitiativeSourceMappings
            .FirstOrDefaultAsync(m => m.Source == source && m.ExternalProjectId.ToLower() == externalProjectId.ToLower(), ct);
        if (taken is not null)
        {
            return RedirectWithError(taken.InitiativeId == id
                ? "That mapping already exists."
                : $"{source} project '{externalProjectId}' is already mapped to another initiative.", id);
        }

        initiative.SourceMappings.Add(new InitiativeSourceMapping { Source = source, ExternalProjectId = externalProjectId });
        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Action = "AddSourceMapping", Source = source, ExternalProjectId = externalProjectId });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Mapped {source} project '{externalProjectId}'. Future imports for it will land here; existing unmapped entries can be assigned from the Actuals page.", id);
    }

    [HttpPost]
    public async Task<IActionResult> RemoveSourceMapping(int id, int mappingId, CancellationToken ct)
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

        var mapping = initiative.SourceMappings.FirstOrDefault(m => m.Id == mappingId);
        if (mapping is null)
        {
            return NotFound();
        }

        initiative.SourceMappings.Remove(mapping);
        db.InitiativeSourceMappings.Remove(mapping);
        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Action = "RemoveSourceMapping", mapping.Source, mapping.ExternalProjectId });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Mapping to {mapping.Source} '{mapping.ExternalProjectId}' removed.", id);
    }

    // ----- Helpers -----

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct, bool includeHistory = false)
    {
        IQueryable<Initiative> query = db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourceType)
            .Include(i => i.NonLaborCosts).ThenInclude(c => c.CostCatalogItem)
            .Include(i => i.Baselines).ThenInclude(b => b.Lines)
            .Include(i => i.Baselines).ThenInclude(b => b.NonLaborLines)
            .Include(i => i.RebaselineRequests)
            .Include(i => i.SourceMappings)
            .AsSplitQuery();
        if (includeHistory)
        {
            query = query.Include(i => i.Phases).ThenInclude(p => p.DateHistory);
        }

        return query.FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) => db.PublishedRateCardsAsync(ct);

    private async Task PopulateEditLists(CancellationToken ct)
    {
        ViewBag.BusinessUnits = await BusinessUnitListAsync(ct, includeInactive: false);
        ViewBag.SizeOptions = await SizeOptionsAsync(ct);
    }

    // Only sizes backed by an allocation template are selectable; conversions just annotate them with hours.
    private async Task<IReadOnlyList<SizeOption>> SizeOptionsAsync(CancellationToken ct)
    {
        var templates = await db.AllocationTemplates.Select(t => new { t.Method, t.SizeKey }).Distinct().ToListAsync(ct);
        var conversions = await db.SizingConversions.ToListAsync(ct);
        return templates
            .Select(t => new SizeOption(t.Method, t.SizeKey,
                conversions.FirstOrDefault(c => c.Method == t.Method && c.Key == t.SizeKey)?.Hours))
            .OrderBy(o => o.Method).ThenBy(o => o.Hours ?? decimal.MaxValue).ThenBy(o => o.Key)
            .ToList();
    }

    private async Task<SelectList> BusinessUnitListAsync(CancellationToken ct, bool includeInactive)
    {
        var query = includeInactive ? db.BusinessUnits : db.BusinessUnits.Where(b => b.IsActive);
        return new SelectList(await query.OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name");
    }

    private async Task PopulateAllocationLists(Initiative initiative, CancellationToken ct)
    {
        ViewBag.Initiative = initiative;
        ViewBag.Phases = new SelectList(initiative.Phases.OrderBy(p => p.Sequence), "Id", "Name");
        ViewBag.ResourceTypes = new SelectList(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name");
        if (initiative.PlanningMode == PlanningMode.FixedDuration)
        {
            var calendar = await workCalendar.GetAsync(ct);
            ViewBag.HoursPerDay = calendar.HoursPerDay;
            ViewBag.PhaseWorkingDays = initiative.Phases.ToDictionary(p => p.Id, p => DurationCalculator.WorkingDays(p.PlannedStart, p.PlannedEnd, calendar.Holidays));
        }
    }

    private async Task ValidateInitiative(InitiativeEditModel model, CancellationToken ct)
    {
        if (!await db.BusinessUnits.AnyAsync(b => b.Id == model.BusinessUnitId && b.IsActive, ct))
        {
            ModelState.AddModelError(nameof(model.BusinessUnitId), "Select an active business unit.");
        }

        ValidateSchedule(model);

        if (model.SizingMethod == SizingMethod.Direct)
        {
            return;
        }

        var key = model.SizeKey?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            ModelState.AddModelError(nameof(model.SizeKey), "Size is required for relative sizing.");
        }
        else if (!await db.AllocationTemplates.AnyAsync(t => t.Method == model.SizingMethod && t.SizeKey == key, ct))
        {
            ModelState.AddModelError(nameof(model.SizeKey), $"'{key}' is not a defined {model.SizingMethod} size. Choose a size that has an allocation template.");
        }
    }

    private void ValidateSchedule(InitiativeEditModel model)
    {
        if (model.PlanningMode != PlanningMode.FixedDuration)
        {
            return;
        }

        if (model.TargetEnd is null)
        {
            ModelState.AddModelError(nameof(model.TargetEnd), "Target end is required for fixed-duration initiatives.");
        }
        else if (model.TargetEnd < model.TargetStart)
        {
            ModelState.AddModelError(nameof(model.TargetEnd), "Target end must be on or after target start.");
        }
    }

    private void ValidateFixedDurationTiling(Initiative initiative, IEnumerable<Phase> phases)
    {
        if (initiative.PlanningMode != PlanningMode.FixedDuration || initiative.TargetEnd is null)
        {
            return;
        }

        var error = DurationCalculator.ValidateTiling(initiative.TargetStart, initiative.TargetEnd.Value, phases);
        if (error is not null)
        {
            ModelState.AddModelError(nameof(PhaseEditModel.PlannedStart), $"Fixed-duration phases must tile the target window contiguously. {error}");
        }
    }

    /// <summary>In fixed-duration mode hours are derived from AllocationPercent × working days; in effort-driven mode they are as entered.</summary>
    private async Task ApplyAllocationEffortAsync(Initiative initiative, InitiativeAllocation allocation, AllocationEditModel model, CancellationToken ct)
    {
        if (initiative.PlanningMode != PlanningMode.FixedDuration)
        {
            allocation.AllocationPercent = null;
            return;
        }

        var phase = initiative.Phases.First(p => p.Id == allocation.PhaseId);
        var calendar = await workCalendar.GetAsync(ct);
        allocation.AllocationPercent = model.AllocationPercent;
        allocation.EstimatedHours = DurationCalculator.Hours(allocation, phase, calendar.Holidays, calendar.HoursPerDay);
    }

    private static int RecomputeFixedDurationHours(Initiative initiative, IEnumerable<InitiativeAllocation> allocations, WorkCalendar calendar)
    {
        var phases = initiative.Phases.ToDictionary(p => p.Id);
        var changed = 0;
        foreach (var allocation in allocations)
        {
            if (allocation.AllocationPercent is null || !phases.TryGetValue(allocation.PhaseId, out var phase))
            {
                continue;
            }

            var hours = DurationCalculator.Hours(allocation, phase, calendar.Holidays, calendar.HoursPerDay);
            if (hours != allocation.EstimatedHours)
            {
                allocation.EstimatedHours = hours;
                changed++;
            }
        }

        return changed;
    }

    private static FixedDurationSummary BuildFixedDurationSummary(Initiative initiative, IReadOnlyList<Phase> phases, ForecastResult forecast, WorkCalendar calendar)
    {
        var end = initiative.TargetEnd!.Value;
        var workingDays = DurationCalculator.WorkingDays(initiative.TargetStart, end, calendar.Holidays);
        var capacity = workingDays * calendar.HoursPerDay;
        return new FixedDurationSummary(
            initiative.TargetStart, end,
            Math.Max(0, end.DayNumber - initiative.TargetStart.DayNumber + 1),
            workingDays, calendar.HoursPerDay,
            capacity <= 0 ? 0 : Math.Round(forecast.TotalHours / capacity, 2, MidpointRounding.AwayFromZero),
            phases.ToDictionary(p => p.Id, p => DurationCalculator.WorkingDays(p.PlannedStart, p.PlannedEnd, calendar.Holidays)));
    }

    private void ValidatePhase(PhaseEditModel model, Initiative initiative)
    {
        if (model.PlannedEnd < model.PlannedStart)
        {
            ModelState.AddModelError(nameof(model.PlannedEnd), "Planned end must be on or after planned start.");
        }

        var name = model.Name.Trim();
        if (initiative.Phases.Any(p => p.Id != model.Id && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ModelState.AddModelError(nameof(model.Name), $"A phase named '{name}' already exists.");
        }
    }

    private async Task ValidateAllocation(AllocationEditModel model, Initiative initiative, CancellationToken ct)
    {
        if (initiative.Phases.All(p => p.Id != model.PhaseId))
        {
            ModelState.AddModelError(nameof(model.PhaseId), initiative.Phases.Count == 0
                ? "Add a phase before adding allocations."
                : "Select a phase that belongs to this initiative.");
        }

        if (!await db.ResourceTypes.AnyAsync(t => t.Id == model.ResourceTypeId, ct))
        {
            ModelState.AddModelError(nameof(model.ResourceTypeId), "Select a resource type.");
        }

        if (initiative.PlanningMode == PlanningMode.FixedDuration)
        {
            if (model.AllocationPercent is null)
            {
                ModelState.AddModelError(nameof(model.AllocationPercent), "Allocation % is required for fixed-duration initiatives; hours are computed from it.");
            }
        }
        else if (model.EstimatedHours < 0.25m)
        {
            ModelState.AddModelError(nameof(model.EstimatedHours), "Hours must be at least 0.25.");
        }
    }

    private Task<InitiativeNonLaborCost?> LoadNonLaborCostAsync(int id, CancellationToken ct) =>
        db.InitiativeNonLaborCosts
            .Include(c => c.Initiative!).ThenInclude(i => i.Members)
            .Include(c => c.Initiative!).ThenInclude(i => i.RebaselineRequests)
            .Include(c => c.Initiative!).ThenInclude(i => i.Phases)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, ct);

    private Task<List<CatalogOption>> CatalogOptionsAsync(CancellationToken ct) =>
        db.CostCatalogItems.Where(i => i.IsActive).OrderBy(i => i.Category).ThenBy(i => i.Name)
            .Select(i => new CatalogOption(i.Id, i.Category, i.Name, i.Vendor, i.BillingModel, i.UnitCost))
            .ToListAsync(ct);

    private static Dictionary<int, (DateOnly Start, DateOnly End)> CostPreviewWindows(Initiative initiative)
    {
        var windows = initiative.Phases.ToDictionary(p => p.Id, p => (p.PlannedStart, p.PlannedEnd));
        windows[0] = (initiative.TargetStart, NonLaborCostCalculator.InitiativeEnd(initiative));
        return windows;
    }

    private async Task PopulateNonLaborLists(Initiative initiative, CancellationToken ct)
    {
        ViewBag.Initiative = initiative;
        ViewBag.Phases = new SelectList(initiative.Phases.OrderBy(p => p.Sequence), "Id", "Name");
        ViewBag.CatalogOptions = await CatalogOptionsAsync(ct);
        ViewBag.CostPreviewWindows = CostPreviewWindows(initiative);
    }

    private async Task ValidateNonLaborCost(NonLaborCostEditModel model, Initiative initiative, CancellationToken ct)
    {
        if (model.Category == CostCategory.Labor)
        {
            ModelState.AddModelError(nameof(model.Category), "Labor is planned through allocations; pick a non-labor category.");
        }

        if (model.PhaseId is { } phaseId && initiative.Phases.All(p => p.Id != phaseId))
        {
            ModelState.AddModelError(nameof(model.PhaseId), "Select a phase that belongs to this initiative, or leave blank for the whole initiative.");
        }

        if (model.CostCatalogItemId is { } catalogId && !await db.CostCatalogItems.AnyAsync(i => i.Id == catalogId, ct))
        {
            ModelState.AddModelError(nameof(model.CostCatalogItemId), "Select a catalog item.");
        }

        if (model.StartDate is null != model.EndDate is null)
        {
            ModelState.AddModelError(nameof(model.EndDate), "Enter both start and end dates, or leave both blank to use the phase/initiative window.");
        }
        else if (model.EndDate < model.StartDate)
        {
            ModelState.AddModelError(nameof(model.EndDate), "End must be on or after start.");
        }
    }

    private static void ApplyNonLaborCost(InitiativeNonLaborCost line, NonLaborCostEditModel model)
    {
        line.PhaseId = model.PhaseId;
        line.CostCatalogItemId = model.CostCatalogItemId;
        line.Category = model.Category;
        line.Description = model.Description.Trim();
        line.BillingModel = model.BillingModel;
        line.Quantity = model.Quantity;
        line.UnitCost = model.UnitCost!.Value;
        line.StartDate = model.StartDate;
        line.EndDate = model.EndDate;
        line.ContractReference = string.IsNullOrWhiteSpace(model.ContractReference) ? null : model.ContractReference.Trim();
        line.CostCenter = string.IsNullOrWhiteSpace(model.CostCenter) ? null : model.CostCenter.Trim();
    }

    private static object NonLaborSnapshot(InitiativeNonLaborCost c) =>
        new { c.InitiativeId, c.PhaseId, c.CostCatalogItemId, c.Category, c.Description, c.BillingModel, c.Quantity, c.UnitCost, c.StartDate, c.EndDate, c.ContractReference, c.CostCenter };

    private string FirstError() =>
        ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault(m => !string.IsNullOrEmpty(m)) ?? "Invalid input.";

    private IActionResult RedirectWithSuccess(string message, int id)
    {
        TempData["Success"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult RedirectWithError(string message, int id)
    {
        TempData["Error"] = message;
        return RedirectToAction(nameof(Details), new { id });
    }

    private static object Snapshot(Initiative i) =>
        new { i.Name, i.Description, i.BusinessUnitId, i.SponsoringTeam, i.SizingMethod, i.SizeKey, i.PlanningMode, i.TargetStart, i.TargetEnd, i.VarianceThresholdPct };

    private static object AllocationSnapshot(InitiativeAllocation a) =>
        new { a.InitiativeId, a.PhaseId, a.ResourceTypeId, a.Seniority, a.Location, a.ResourcingClass, a.Quantity, a.AllocationPercent, a.EstimatedHours, a.ContractReference, a.CostCenter };

    private static List<RollupRow> Rollup(ForecastResult forecast, Func<ForecastLine, string> key, IEnumerable<string>? order = null)
    {
        var rows = forecast.Lines.GroupBy(key)
            .Select(g => new RollupRow(g.Key, g.Sum(l => l.Hours), g.Sum(l => l.Cost), g.Any(l => l.IsUnpriced)))
            .ToList();
        if (order is null)
        {
            return rows.OrderByDescending(r => r.Cost).ToList();
        }

        var rank = order.Select((name, idx) => (name, idx)).ToDictionary(x => x.name, x => x.idx);
        return rows.OrderBy(r => rank.GetValueOrDefault(r.Label, int.MaxValue)).ToList();
    }

    private static List<GanttBar> BuildGantt(IReadOnlyList<Phase> phases)
    {
        if (phases.Count == 0)
        {
            return [];
        }

        var start = phases.Min(p => p.PlannedStart);
        var end = phases.Max(p => p.PlannedEnd);
        var span = Math.Max(1, end.DayNumber - start.DayNumber + 1);
        return phases.Select(p => new GanttBar(
            p,
            100.0 * (p.PlannedStart.DayNumber - start.DayNumber) / span,
            Math.Max(1.0, 100.0 * (p.PlannedEnd.DayNumber - p.PlannedStart.DayNumber + 1) / span))).ToList();
    }
}
