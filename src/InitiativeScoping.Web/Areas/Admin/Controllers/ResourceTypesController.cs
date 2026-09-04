using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class ResourceTypesController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.ResourceTypes
            .Include(t => t.Discipline)
            .OrderBy(t => t.Discipline!.Name).ThenBy(t => t.Name)
            .Select(t => new ResourceTypeListItem
            {
                Type = t,
                ReferenceCount = db.RateCardEntries.Count(e => e.ResourceTypeId == t.Id)
                                 + db.InitiativeAllocations.Count(a => a.ResourceTypeId == t.Id)
                                 + db.People.Count(p => p.ResourceTypeId == t.Id)
                                 + db.AllocationTemplateLines.Count(l => l.ResourceTypeId == t.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public async Task<IActionResult> Create(CancellationToken ct) => View("Edit", await WithDisciplines(new ResourceTypeEditModel(), ct));

    [HttpPost]
    public async Task<IActionResult> Create(ResourceTypeEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        var discipline = await ValidateDiscipline(model, ct);
        if (!ModelState.IsValid || discipline is null)
        {
            return View("Edit", await WithDisciplines(model, ct));
        }

        var type = new ResourceType { Name = model.Name.Trim(), DisciplineId = discipline.Id, IsActive = model.IsActive };
        db.ResourceTypes.Add(type);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(ResourceType), type.Id, AuditActions.Create, new { type.Name, Discipline = discipline.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resource type '{type.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var type = await db.ResourceTypes.FindAsync([id], ct);
        if (type is null)
        {
            return NotFound();
        }

        return View(await WithDisciplines(new ResourceTypeEditModel { Id = type.Id, Name = type.Name, DisciplineId = type.DisciplineId, IsActive = type.IsActive }, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ResourceTypeEditModel model, CancellationToken ct)
    {
        var type = await db.ResourceTypes.Include(t => t.Discipline).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (type is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        var discipline = await ValidateDiscipline(model, ct, currentDisciplineId: type.DisciplineId);
        if (!ModelState.IsValid || discipline is null)
        {
            return View(await WithDisciplines(model, ct));
        }

        var before = new { type.Name, Discipline = type.Discipline!.Name, type.IsActive };
        type.Name = model.Name.Trim();
        type.DisciplineId = discipline.Id;
        type.Discipline = discipline;
        type.IsActive = model.IsActive;
        audit.Record(nameof(ResourceType), type.Id, AuditActions.Update, new { Before = before, After = new { type.Name, Discipline = discipline.Name, type.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resource type '{type.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var type = await db.ResourceTypes.FindAsync([id], ct);
        if (type is null)
        {
            return NotFound();
        }

        var referenced = await db.RateCardEntries.AnyAsync(e => e.ResourceTypeId == id, ct)
                         || await db.InitiativeAllocations.AnyAsync(a => a.ResourceTypeId == id, ct)
                         || await db.People.AnyAsync(p => p.ResourceTypeId == id, ct)
                         || await db.AllocationTemplateLines.AnyAsync(l => l.ResourceTypeId == id, ct);
        if (referenced)
        {
            return RedirectWithError($"'{type.Name}' is referenced by rate cards, initiatives, templates or people and cannot be deleted. Deactivate it instead.");
        }

        db.ResourceTypes.Remove(type);
        audit.Record(nameof(ResourceType), type.Id, AuditActions.Delete, new { type.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resource type '{type.Name}' deleted.");
    }

    private async Task<Discipline?> ValidateDiscipline(ResourceTypeEditModel model, CancellationToken ct, int? currentDisciplineId = null)
    {
        if (model.DisciplineId is not > 0)
        {
            return null;
        }

        var discipline = await db.Disciplines.FindAsync([model.DisciplineId.Value], ct);
        if (discipline is null || (!discipline.IsActive && discipline.Id != currentDisciplineId))
        {
            ModelState.AddModelError(nameof(model.DisciplineId), "Select an active discipline.");
            return null;
        }

        return discipline;
    }

    private async Task<ResourceTypeEditModel> WithDisciplines(ResourceTypeEditModel model, CancellationToken ct)
    {
        model.Disciplines = await db.Disciplines
            .Where(d => d.IsActive || d.Id == model.DisciplineId)
            .OrderBy(d => d.Name)
            .Select(d => new SelectListItem(d.IsActive ? d.Name : d.Name + " (inactive)", d.Id.ToString(), d.Id == model.DisciplineId))
            .ToListAsync(ct);
        return model;
    }

    private async Task ValidateUniqueName(ResourceTypeEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.ResourceTypes.AnyAsync(t => t.Id != model.Id && t.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A resource type with this name already exists.");
        }
    }
}
