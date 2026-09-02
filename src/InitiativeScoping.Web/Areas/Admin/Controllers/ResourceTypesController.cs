using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class ResourceTypesController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.ResourceTypes
            .OrderBy(t => t.Discipline).ThenBy(t => t.Name)
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

    public IActionResult Create() => View("Edit", new ResourceTypeEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(ResourceTypeEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var type = new ResourceType { Name = model.Name.Trim(), Discipline = model.Discipline.Trim(), IsActive = model.IsActive };
        db.ResourceTypes.Add(type);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(ResourceType), type.Id, AuditActions.Create, new { type.Name, type.Discipline });
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

        return View(new ResourceTypeEditModel { Id = type.Id, Name = type.Name, Discipline = type.Discipline, IsActive = type.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ResourceTypeEditModel model, CancellationToken ct)
    {
        var type = await db.ResourceTypes.FindAsync([id], ct);
        if (type is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { type.Name, type.Discipline, type.IsActive };
        type.Name = model.Name.Trim();
        type.Discipline = model.Discipline.Trim();
        type.IsActive = model.IsActive;
        audit.Record(nameof(ResourceType), type.Id, AuditActions.Update, new { Before = before, After = new { type.Name, type.Discipline, type.IsActive } });
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

    private async Task ValidateUniqueName(ResourceTypeEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim();
        if (await db.ResourceTypes.AnyAsync(t => t.Id != model.Id && t.Name == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A resource type with this name already exists.");
        }
    }
}
