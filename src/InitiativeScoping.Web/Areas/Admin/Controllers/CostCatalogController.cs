using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class CostCatalogController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.CostCatalogItems
            .OrderBy(i => i.Category).ThenBy(i => i.Name)
            .Select(i => new CostCatalogItemListItem
            {
                Item = i,
                ReferenceCount = db.InitiativeNonLaborCosts.Count(c => c.CostCatalogItemId == i.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public IActionResult Create() => View("Edit", new CostCatalogItemEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(CostCatalogItemEditModel model, CancellationToken ct)
    {
        await Validate(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var item = new CostCatalogItem { Name = model.Name.Trim() };
        Apply(item, model);
        db.CostCatalogItems.Add(item);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(CostCatalogItem), item.Id, AuditActions.Create, Snapshot(item));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Catalog item '{item.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var item = await db.CostCatalogItems.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }

        return View(new CostCatalogItemEditModel
        {
            Id = item.Id,
            Category = item.Category,
            Name = item.Name,
            Vendor = item.Vendor,
            BillingModel = item.BillingModel,
            UnitCost = item.UnitCost,
            IsActive = item.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, CostCatalogItemEditModel model, CancellationToken ct)
    {
        var item = await db.CostCatalogItems.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }

        model.Id = id;
        await Validate(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = Snapshot(item);
        Apply(item, model);
        audit.Record(nameof(CostCatalogItem), item.Id, AuditActions.Update, new { Before = before, After = Snapshot(item) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Catalog item '{item.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var item = await db.CostCatalogItems.FindAsync([id], ct);
        if (item is null)
        {
            return NotFound();
        }

        if (await db.InitiativeNonLaborCosts.AnyAsync(c => c.CostCatalogItemId == id, ct))
        {
            return RedirectWithError($"'{item.Name}' is referenced by initiative cost lines and cannot be deleted. Deactivate it instead.");
        }

        db.CostCatalogItems.Remove(item);
        audit.Record(nameof(CostCatalogItem), item.Id, AuditActions.Delete, Snapshot(item));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Catalog item '{item.Name}' deleted.");
    }

    private static void Apply(CostCatalogItem item, CostCatalogItemEditModel model)
    {
        item.Category = model.Category!.Value;
        item.Name = model.Name.Trim();
        item.Vendor = string.IsNullOrWhiteSpace(model.Vendor) ? null : model.Vendor.Trim();
        item.BillingModel = model.BillingModel!.Value;
        item.UnitCost = model.UnitCost!.Value;
        item.IsActive = model.IsActive;
    }

    private static object Snapshot(CostCatalogItem item) =>
        new { item.Category, item.Name, item.Vendor, item.BillingModel, item.UnitCost, item.IsActive };

    private async Task Validate(CostCatalogItemEditModel model, CancellationToken ct)
    {
        if (model.Category == CostCategory.Labor)
        {
            ModelState.AddModelError(nameof(model.Category), "Labor is priced from rate cards; pick a non-labor category.");
        }

        var name = model.Name.Trim().ToLowerInvariant();
        if (model.Category is { } category
            && await db.CostCatalogItems.AnyAsync(i => i.Id != model.Id && i.Category == category && i.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "An item with this name already exists in this category.");
        }
    }
}
