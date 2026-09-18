using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class ResourcingClassesController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.ResourcingClasses
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new ResourcingClassListItem
            {
                Class = c,
                RateCardEntries = db.RateCardEntries.Count(e => e.ResourcingClassId == c.Id),
                Allocations = db.InitiativeAllocations.Count(a => a.ResourcingClassId == c.Id),
                People = db.People.Count(p => p.ResourcingClassId == c.Id),
                BaselineLines = db.ForecastBaselineLines.Count(l => l.ResourcingClassId == c.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var next = await db.ResourcingClasses.Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? 0;
        return View("Edit", new ResourcingClassEditModel { SortOrder = next + 1 });
    }

    [HttpPost]
    public async Task<IActionResult> Create(ResourcingClassEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var cls = new ResourcingClass
        {
            Name = model.Name.Trim(),
            IsVendor = model.IsVendor,
            DefaultCapexPercent = model.DefaultCapexPercent,
            SortOrder = model.SortOrder,
            IsActive = model.IsActive
        };
        db.ResourcingClasses.Add(cls);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(ResourcingClass), cls.Id, AuditActions.Create, Snapshot(cls));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resourcing class '{cls.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var cls = await db.ResourcingClasses.FindAsync([id], ct);
        if (cls is null)
        {
            return NotFound();
        }

        return View(new ResourcingClassEditModel
        {
            Id = cls.Id,
            Name = cls.Name,
            IsVendor = cls.IsVendor,
            DefaultCapexPercent = cls.DefaultCapexPercent,
            SortOrder = cls.SortOrder,
            IsActive = cls.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ResourcingClassEditModel model, CancellationToken ct)
    {
        var cls = await db.ResourcingClasses.FindAsync([id], ct);
        if (cls is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (model.IsVendor != cls.IsVendor && await IsReferencedAsync(cls, ct))
        {
            ModelState.AddModelError(nameof(model.IsVendor), "Vendor-backed cannot change while rate cards, allocations, people or baselines use this class.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = Snapshot(cls);
        cls.Name = model.Name.Trim();
        cls.IsVendor = model.IsVendor;
        cls.DefaultCapexPercent = model.DefaultCapexPercent;
        cls.SortOrder = model.SortOrder;
        cls.IsActive = model.IsActive;
        audit.Record(nameof(ResourcingClass), cls.Id, AuditActions.Update, new { Before = before, After = Snapshot(cls) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resourcing class '{cls.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var cls = await db.ResourcingClasses.FindAsync([id], ct);
        if (cls is null)
        {
            return NotFound();
        }

        if (await IsReferencedAsync(cls, ct))
        {
            return RedirectWithError($"'{cls.Name}' is referenced by rate cards, allocations, people or baselines and cannot be deleted. Deactivate it instead.");
        }

        db.ResourcingClasses.Remove(cls);
        audit.Record(nameof(ResourcingClass), cls.Id, AuditActions.Delete, new { cls.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Resourcing class '{cls.Name}' deleted.");
    }

    [HttpPost]
    public Task<IActionResult> BulkDelete(int[] ids, CancellationToken ct) => BulkDeleteRows(
        db, db.ResourcingClasses, ids, x => c => x.Contains(c.Id),
        async (c, token) => !await IsReferencedAsync(c, token),
        c => c.Name,
        c => audit.Record(nameof(ResourcingClass), c.Id, AuditActions.Delete, new { c.Name }),
        "resourcing class", "resourcing classes", "referenced; deactivate instead", ct);

    [HttpPost]
    public Task<IActionResult> BulkActivate(int[] ids, CancellationToken ct) => SetActive(ids, true, ct);

    [HttpPost]
    public Task<IActionResult> BulkDeactivate(int[] ids, CancellationToken ct) => SetActive(ids, false, ct);

    private Task<IActionResult> SetActive(int[] ids, bool active, CancellationToken ct) => BulkSetActive(
        db, db.ResourcingClasses, ids, x => c => x.Contains(c.Id),
        c => c.IsActive, (c, a) => c.IsActive = a,
        (c, a) => audit.Record(nameof(ResourcingClass), c.Id, AuditActions.Update, new { Before = new { IsActive = !a }, After = new { IsActive = a } }),
        active, "resourcing class", "resourcing classes", ct);

    private static object Snapshot(ResourcingClass c) => new { c.Name, c.IsVendor, c.DefaultCapexPercent, c.SortOrder, c.IsActive };

    private async Task<bool> IsReferencedAsync(ResourcingClass cls, CancellationToken ct) =>
        await db.RateCardEntries.AnyAsync(e => e.ResourcingClassId == cls.Id, ct)
        || await db.InitiativeAllocations.AnyAsync(a => a.ResourcingClassId == cls.Id, ct)
        || await db.People.AnyAsync(p => p.ResourcingClassId == cls.Id, ct)
        || await db.ForecastBaselineLines.AnyAsync(l => l.ResourcingClassId == cls.Id, ct);

    private async Task ValidateUniqueName(ResourcingClassEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.ResourcingClasses.AnyAsync(c => c.Id != model.Id && c.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A resourcing class with this name already exists.");
        }
    }
}
