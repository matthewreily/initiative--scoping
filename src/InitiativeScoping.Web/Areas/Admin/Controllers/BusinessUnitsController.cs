using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class BusinessUnitsController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.BusinessUnits
            .OrderBy(b => b.Name)
            .Select(b => new BusinessUnitListItem
            {
                Unit = b,
                ReferenceCount = db.RateCardEntries.Count(e => e.BusinessUnitId == b.Id)
                                 + db.Initiatives.Count(i => i.BusinessUnitId == b.Id)
                                 + db.InitiativeBusinessUnits.Count(i => i.BusinessUnitId == b.Id)
                                 + db.InitiativeAllocations.Count(a => a.BusinessUnitId == b.Id)
                                 + db.People.Count(p => p.BusinessUnitId == b.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public IActionResult Create() => View("Edit", new BusinessUnitEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(BusinessUnitEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var unit = new BusinessUnit { Name = model.Name.Trim(), IsActive = model.IsActive };
        db.BusinessUnits.Add(unit);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(BusinessUnit), unit.Id, AuditActions.Create, new { unit.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Business unit '{unit.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var unit = await db.BusinessUnits.FindAsync([id], ct);
        if (unit is null)
        {
            return NotFound();
        }

        return View(new BusinessUnitEditModel { Id = unit.Id, Name = unit.Name, IsActive = unit.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, BusinessUnitEditModel model, CancellationToken ct)
    {
        var unit = await db.BusinessUnits.FindAsync([id], ct);
        if (unit is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { unit.Name, unit.IsActive };
        unit.Name = model.Name.Trim();
        unit.IsActive = model.IsActive;
        audit.Record(nameof(BusinessUnit), unit.Id, AuditActions.Update, new { Before = before, After = new { unit.Name, unit.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Business unit '{unit.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var unit = await db.BusinessUnits.FindAsync([id], ct);
        if (unit is null)
        {
            return NotFound();
        }

        var referenced = await db.RateCardEntries.AnyAsync(e => e.BusinessUnitId == id, ct)
                         || await db.Initiatives.AnyAsync(i => i.BusinessUnitId == id, ct)
                         || await db.InitiativeBusinessUnits.AnyAsync(i => i.BusinessUnitId == id, ct)
                         || await db.InitiativeAllocations.AnyAsync(a => a.BusinessUnitId == id, ct)
                         || await db.People.AnyAsync(p => p.BusinessUnitId == id, ct);
        if (referenced)
        {
            return RedirectWithError($"'{unit.Name}' is referenced by rate cards, initiatives or people and cannot be deleted. Deactivate it instead.");
        }

        db.BusinessUnits.Remove(unit);
        audit.Record(nameof(BusinessUnit), unit.Id, AuditActions.Delete, new { unit.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Business unit '{unit.Name}' deleted.");
    }

    [HttpPost]
    public Task<IActionResult> BulkDelete(int[] ids, CancellationToken ct) => BulkDeleteRows(
        db, db.BusinessUnits, ids, x => u => x.Contains(u.Id),
        async (u, c) => !(await db.RateCardEntries.AnyAsync(e => e.BusinessUnitId == u.Id, c)
                          || await db.Initiatives.AnyAsync(i => i.BusinessUnitId == u.Id, c)
                          || await db.InitiativeBusinessUnits.AnyAsync(i => i.BusinessUnitId == u.Id, c)
                          || await db.InitiativeAllocations.AnyAsync(a => a.BusinessUnitId == u.Id, c)
                          || await db.People.AnyAsync(p => p.BusinessUnitId == u.Id, c)),
        u => u.Name,
        u => audit.Record(nameof(BusinessUnit), u.Id, AuditActions.Delete, new { u.Name }),
        "business unit", "business units", "referenced; deactivate instead", ct);

    [HttpPost]
    public Task<IActionResult> BulkActivate(int[] ids, CancellationToken ct) => SetActive(ids, true, ct);

    [HttpPost]
    public Task<IActionResult> BulkDeactivate(int[] ids, CancellationToken ct) => SetActive(ids, false, ct);

    private Task<IActionResult> SetActive(int[] ids, bool active, CancellationToken ct) => BulkSetActive(
        db, db.BusinessUnits, ids, x => u => x.Contains(u.Id),
        u => u.IsActive, (u, a) => u.IsActive = a,
        (u, a) => audit.Record(nameof(BusinessUnit), u.Id, AuditActions.Update, new { Before = new { IsActive = !a }, After = new { IsActive = a } }),
        active, "business unit", "business units", ct);

    private async Task ValidateUniqueName(BusinessUnitEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.BusinessUnits.AnyAsync(b => b.Id != model.Id && b.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A business unit with this name already exists.");
        }
    }
}
