using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class DisciplinesController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.Disciplines
            .OrderBy(d => d.Name)
            .Select(d => new DisciplineListItem
            {
                Discipline = d,
                ReferenceCount = db.ResourceTypes.Count(t => t.DisciplineId == d.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public IActionResult Create() => View("Edit", new DisciplineEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(DisciplineEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var discipline = new Discipline { Name = model.Name.Trim(), IsActive = model.IsActive };
        db.Disciplines.Add(discipline);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Discipline), discipline.Id, AuditActions.Create, new { discipline.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Discipline '{discipline.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var discipline = await db.Disciplines.FindAsync([id], ct);
        if (discipline is null)
        {
            return NotFound();
        }

        return View(new DisciplineEditModel { Id = discipline.Id, Name = discipline.Name, IsActive = discipline.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, DisciplineEditModel model, CancellationToken ct)
    {
        var discipline = await db.Disciplines.FindAsync([id], ct);
        if (discipline is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { discipline.Name, discipline.IsActive };
        discipline.Name = model.Name.Trim();
        discipline.IsActive = model.IsActive;
        audit.Record(nameof(Discipline), discipline.Id, AuditActions.Update, new { Before = before, After = new { discipline.Name, discipline.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Discipline '{discipline.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var discipline = await db.Disciplines.FindAsync([id], ct);
        if (discipline is null)
        {
            return NotFound();
        }

        if (await db.ResourceTypes.AnyAsync(t => t.DisciplineId == id, ct))
        {
            return RedirectWithError($"'{discipline.Name}' is referenced by resource types and cannot be deleted. Deactivate it instead.");
        }

        db.Disciplines.Remove(discipline);
        audit.Record(nameof(Discipline), discipline.Id, AuditActions.Delete, new { discipline.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Discipline '{discipline.Name}' deleted.");
    }

    private async Task ValidateUniqueName(DisciplineEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.Disciplines.AnyAsync(d => d.Id != model.Id && d.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A discipline with this name already exists.");
        }
    }
}
