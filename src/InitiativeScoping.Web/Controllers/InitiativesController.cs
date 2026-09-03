using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class InitiativesController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock) : Controller
{
    private const string Entity = nameof(Initiative);
    private const string ScopeLockedMessage = "Scope is locked; it can only change in Draft or during an approved re-baseline.";

    // ----- List / create / edit -----

    public async Task<IActionResult> Index(InitiativeStatus? status, int? businessUnitId, string? search, CancellationToken ct)
    {
        var query = db.Initiatives.Include(i => i.BusinessUnit).Include(i => i.Phases).Include(i => i.Allocations).AsQueryable();
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
        await PopulateBusinessUnits(ct);
        return View("Edit", new InitiativeEditModel());
    }

    [HttpPost, Authorize(Policy = AppPolicies.CanEditInitiatives)]
    public async Task<IActionResult> Create(InitiativeEditModel model, CancellationToken ct)
    {
        await ValidateInitiative(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateBusinessUnits(ct);
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
            TargetStart = model.TargetStart,
            VarianceThresholdPct = model.VarianceThresholdPct,
            CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow()
        };
        initiative.Members.Add(new InitiativeMember { UserId = currentUser.UserId, Role = InitiativeMemberRole.Owner });
        db.Initiatives.Add(initiative);
        await db.SaveChangesAsync(ct);
        audit.Record(Entity, initiative.Id, AuditActions.Create, new { initiative.Name, initiative.BusinessUnitId, initiative.SizingMethod, initiative.SizeKey, initiative.TargetStart });
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

        await PopulateBusinessUnits(ct);
        return View(new InitiativeEditModel
        {
            Id = initiative.Id, Name = initiative.Name, Description = initiative.Description, BusinessUnitId = initiative.BusinessUnitId,
            SponsoringTeam = initiative.SponsoringTeam, SizingMethod = initiative.SizingMethod, SizeKey = initiative.SizeKey,
            TargetStart = initiative.TargetStart, VarianceThresholdPct = initiative.VarianceThresholdPct
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
        if (!ModelState.IsValid)
        {
            await PopulateBusinessUnits(ct);
            return View(model);
        }

        var before = Snapshot(initiative);
        initiative.Name = model.Name.Trim();
        initiative.Description = model.Description?.Trim();
        initiative.BusinessUnitId = model.BusinessUnitId;
        initiative.SponsoringTeam = model.SponsoringTeam?.Trim();
        initiative.SizingMethod = model.SizingMethod;
        initiative.SizeKey = model.SizingMethod == SizingMethod.Direct ? null : model.SizeKey?.Trim();
        initiative.TargetStart = model.TargetStart;
        initiative.VarianceThresholdPct = model.VarianceThresholdPct;
        audit.Record(Entity, initiative.Id, AuditActions.Update, new { Before = before, After = Snapshot(initiative) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Initiative updated.", id);
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

        var model = new InitiativeDetailsModel
        {
            Initiative = initiative,
            Forecast = forecast,
            ByPhase = Rollup(forecast, l => phaseNames.GetValueOrDefault(l.Allocation.PhaseId, "?"),
                orderedPhases.Select(p => p.Name)),
            ByResourceType = Rollup(forecast, l => typeNames.GetValueOrDefault(l.Allocation.ResourceTypeId, "?")),
            ByClass = Rollup(forecast, l => l.Allocation.ResourcingClass == ResourcingClass.InternalFte ? "Internal FTE" : "Vendor"),
            Gantt = BuildGantt(orderedPhases),
            ResourceTypeNames = typeNames,
            NewPhase = new PhaseEditModel
            {
                InitiativeId = id,
                PlannedStart = orderedPhases.LastOrDefault()?.PlannedEnd.AddDays(1) ?? initiative.TargetStart,
                PlannedEnd = (orderedPhases.LastOrDefault()?.PlannedEnd.AddDays(1) ?? initiative.TargetStart).AddDays(29)
            },
            NewAllocation = new AllocationEditModel { InitiativeId = id },
            NewMember = new MemberEditModel { InitiativeId = id },
            ApplySize = new ApplySizeModel
            {
                InitiativeId = id,
                Method = initiative.SizingMethod == SizingMethod.Direct ? SizingMethod.TShirt : initiative.SizingMethod,
                SizeKey = initiative.SizeKey ?? string.Empty
            },
            Phases = new SelectList(orderedPhases, "Id", "Name"),
            ResourceTypes = new SelectList(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name"),
            Conversions = (await db.SizingConversions.ToListAsync(ct)).OrderBy(c => c.Method).ThenBy(c => c.Hours).ToList(),
            CanEdit = InitiativeAccess.CanEdit(currentUser, initiative),
            CanManage = InitiativeAccess.CanManage(currentUser, initiative),
            ScopeEditable = InitiativeAccess.IsScopeEditable(initiative),
            CanApproveRebaseline = InitiativeAccess.CanApproveRebaseline(currentUser),
            ActivationBlockers = initiative.Status == InitiativeStatus.Draft ? InitiativeLifecycle.BaselineBlockers(initiative, forecast) : [],
            StatusTransitions = InitiativeLifecycle.AllowedTransitions(initiative.Status)
                .Where(s => !(initiative.Status == InitiativeStatus.Draft && s == InitiativeStatus.Active)).ToList()
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
        if (!ModelState.IsValid)
        {
            return RedirectWithError(FirstError(), id);
        }

        var phase = new Phase
        {
            Name = model.Name.Trim(),
            Sequence = (initiative.Phases.Count == 0 ? 0 : initiative.Phases.Max(p => p.Sequence)) + 1,
            PlannedStart = model.PlannedStart,
            PlannedEnd = model.PlannedEnd
        };
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
        audit.Record(nameof(Phase), phase.Id, AuditActions.Update, new { initiative.Id, Before = before, After = new { phase.Name, phase.PlannedStart, phase.PlannedEnd }, model.Reason });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Phase '{phase.Name}' updated.", initiative.Id);
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
            Quantity = allocation.Quantity, EstimatedHours = allocation.EstimatedHours,
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

        var phasesByName = initiative.Phases.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var nextSequence = (initiative.Phases.Count == 0 ? 0 : initiative.Phases.Max(p => p.Sequence)) + 1;
        var nextStart = initiative.Phases.Count == 0 ? initiative.TargetStart : initiative.Phases.Max(p => p.PlannedEnd).AddDays(1);
        var createdPhases = 0;
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

        var location = model.Location.Trim();
        foreach (var sized in SizingApplier.Apply(conversion.Hours, lines))
        {
            initiative.Allocations.Add(new InitiativeAllocation
            {
                Phase = phasesByName[sized.PhaseName], ResourceTypeId = sized.ResourceTypeId, Seniority = sized.Seniority,
                Location = location, ResourcingClass = model.ResourcingClass, Quantity = 1, EstimatedHours = sized.Hours
            });
        }

        initiative.SizingMethod = model.Method;
        initiative.SizeKey = key;
        audit.Record(Entity, initiative.Id, AuditActions.Update, new
        {
            Action = "ApplySize", model.Method, SizeKey = key, conversion.Hours, TemplateId = template.Id, model.Replace, PhasesCreated = createdPhases, Lines = lines.Count
        });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Applied {model.Method} '{key}' ({conversion.Hours}h) using template '{template.Name}': {lines.Count} allocation(s), {createdPhases} new phase(s). Adjust as needed.", id);
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

    // ----- Helpers -----

    private Task<Initiative?> LoadAsync(int id, CancellationToken ct, bool includeHistory = false)
    {
        IQueryable<Initiative> query = db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourceType)
            .Include(i => i.Baselines)
            .Include(i => i.RebaselineRequests);
        if (includeHistory)
        {
            query = query.Include(i => i.Phases).ThenInclude(p => p.DateHistory);
        }

        return query.FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) =>
        db.RateCards.Include(c => c.Entries).Where(c => c.Status == RateCardStatus.Published).AsNoTracking().ToListAsync(ct);

    private async Task PopulateBusinessUnits(CancellationToken ct) => ViewBag.BusinessUnits = await BusinessUnitListAsync(ct, includeInactive: false);

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
    }

    private async Task ValidateInitiative(InitiativeEditModel model, CancellationToken ct)
    {
        if (!await db.BusinessUnits.AnyAsync(b => b.Id == model.BusinessUnitId && b.IsActive, ct))
        {
            ModelState.AddModelError(nameof(model.BusinessUnitId), "Select an active business unit.");
        }

        if (model.SizingMethod != SizingMethod.Direct && string.IsNullOrWhiteSpace(model.SizeKey))
        {
            ModelState.AddModelError(nameof(model.SizeKey), "Size is required for relative sizing.");
        }
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
    }

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
        new { i.Name, i.Description, i.BusinessUnitId, i.SponsoringTeam, i.SizingMethod, i.SizeKey, i.TargetStart, i.VarianceThresholdPct };

    private static object AllocationSnapshot(InitiativeAllocation a) =>
        new { a.InitiativeId, a.PhaseId, a.ResourceTypeId, a.Seniority, a.Location, a.ResourcingClass, a.Quantity, a.EstimatedHours, a.ContractReference, a.CostCenter };

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
