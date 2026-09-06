using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class VendorsController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.Vendors
            .OrderBy(v => v.Name)
            .Select(v => new VendorListItem
            {
                Vendor = v,
                RateCardEntries = db.RateCardEntries.Count(e => e.VendorId == v.Id),
                Allocations = db.InitiativeAllocations.Count(a => a.VendorId == v.Id),
                People = db.People.Count(p => p.VendorId == v.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public IActionResult Create() => View("Edit", new VendorEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(VendorEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var vendor = new Vendor { Name = model.Name.Trim(), IsActive = model.IsActive };
        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Vendor), vendor.Id, AuditActions.Create, new { vendor.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Vendor '{vendor.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var vendor = await db.Vendors.FindAsync([id], ct);
        if (vendor is null)
        {
            return NotFound();
        }

        return View(new VendorEditModel { Id = vendor.Id, Name = vendor.Name, IsActive = vendor.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, VendorEditModel model, CancellationToken ct)
    {
        var vendor = await db.Vendors.FindAsync([id], ct);
        if (vendor is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { vendor.Name, vendor.IsActive };
        vendor.Name = model.Name.Trim();
        vendor.IsActive = model.IsActive;
        audit.Record(nameof(Vendor), vendor.Id, AuditActions.Update, new { Before = before, After = new { vendor.Name, vendor.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Vendor '{vendor.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var vendor = await db.Vendors.FindAsync([id], ct);
        if (vendor is null)
        {
            return NotFound();
        }

        var referenced = await db.RateCardEntries.AnyAsync(e => e.VendorId == id, ct)
            || await db.InitiativeAllocations.AnyAsync(a => a.VendorId == id, ct)
            || await db.People.AnyAsync(p => p.VendorId == id, ct);
        if (referenced)
        {
            return RedirectWithError($"'{vendor.Name}' is referenced by rate cards, allocations or people and cannot be deleted. Deactivate it instead.");
        }

        db.Vendors.Remove(vendor);
        audit.Record(nameof(Vendor), vendor.Id, AuditActions.Delete, new { vendor.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Vendor '{vendor.Name}' deleted.");
    }

    private async Task ValidateUniqueName(VendorEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.Vendors.AnyAsync(v => v.Id != model.Id && v.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A vendor with this name already exists.");
        }
    }
}
