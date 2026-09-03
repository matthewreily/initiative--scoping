using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class PeopleController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(string? search, CancellationToken ct)
    {
        var query = db.People.Include(p => p.ResourceType).Include(p => p.BusinessUnit).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p => p.DisplayName.ToLower().Contains(s) || (p.ExternalIds != null && p.ExternalIds.ToLower().Contains(s)));
        }

        var items = await query
            .OrderBy(p => p.DisplayName)
            .Select(p => new PersonListItem { Person = p, EntryCount = db.ActualEntries.Count(e => e.PersonId == p.Id) })
            .ToListAsync(ct);
        ViewBag.Search = search;
        return View(items);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await PopulateLists(ct);
        return View("Edit", new PersonEditModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(PersonEditModel model, CancellationToken ct)
    {
        await Validate(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateLists(ct);
            return View("Edit", model);
        }

        var person = new Person
        {
            DisplayName = model.DisplayName.Trim(),
            ExternalIds = NormalizeIds(model.ExternalIds),
            ResourceTypeId = model.ResourceTypeId,
            BusinessUnitId = model.BusinessUnitId,
            Seniority = model.Seniority,
            Location = model.Location.Trim(),
            ResourcingClass = model.ResourcingClass,
            IsActive = model.IsActive
        };
        db.People.Add(person);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Person), person.Id, AuditActions.Create, Snapshot(person));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Person '{person.DisplayName}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var person = await db.People.FindAsync([id], ct);
        if (person is null)
        {
            return NotFound();
        }

        await PopulateLists(ct);
        return View(new PersonEditModel
        {
            Id = person.Id,
            DisplayName = person.DisplayName,
            ExternalIds = person.ExternalIds,
            ResourceTypeId = person.ResourceTypeId,
            BusinessUnitId = person.BusinessUnitId,
            Seniority = person.Seniority,
            Location = person.Location,
            ResourcingClass = person.ResourcingClass,
            IsActive = person.IsActive
        });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, PersonEditModel model, CancellationToken ct)
    {
        var person = await db.People.FindAsync([id], ct);
        if (person is null)
        {
            return NotFound();
        }

        model.Id = id;
        await Validate(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateLists(ct);
            return View(model);
        }

        var before = Snapshot(person);
        person.DisplayName = model.DisplayName.Trim();
        person.ExternalIds = NormalizeIds(model.ExternalIds);
        person.ResourceTypeId = model.ResourceTypeId;
        person.BusinessUnitId = model.BusinessUnitId;
        person.Seniority = model.Seniority;
        person.Location = model.Location.Trim();
        person.ResourcingClass = model.ResourcingClass;
        person.IsActive = model.IsActive;
        audit.Record(nameof(Person), person.Id, AuditActions.Update, new { Before = before, After = Snapshot(person) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Person '{person.DisplayName}' updated. Existing actuals keep their calculated cost; re-map an entry to re-price it.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var person = await db.People.FindAsync([id], ct);
        if (person is null)
        {
            return NotFound();
        }

        if (await db.ActualEntries.AnyAsync(e => e.PersonId == id, ct))
        {
            return RedirectWithError($"'{person.DisplayName}' has imported actuals and cannot be deleted. Deactivate instead.");
        }

        db.People.Remove(person);
        audit.Record(nameof(Person), person.Id, AuditActions.Delete, new { person.DisplayName });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Person '{person.DisplayName}' deleted.");
    }

    private async Task Validate(PersonEditModel model, CancellationToken ct)
    {
        if (!await db.ResourceTypes.AnyAsync(t => t.Id == model.ResourceTypeId, ct))
        {
            ModelState.AddModelError(nameof(model.ResourceTypeId), "Choose a resource type.");
        }

        if (!await db.BusinessUnits.AnyAsync(b => b.Id == model.BusinessUnitId, ct))
        {
            ModelState.AddModelError(nameof(model.BusinessUnitId), "Choose a business unit.");
        }

        var ids = (NormalizeIds(model.ExternalIds) ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length == 0)
        {
            return;
        }

        var others = await db.People.Where(p => p.Id != model.Id && p.ExternalIds != null).ToListAsync(ct);
        var clash = ids.FirstOrDefault(id => others.Any(o => ActualsCosting.MatchesExternalId(o, id)));
        if (clash is not null)
        {
            var owner = others.First(o => ActualsCosting.MatchesExternalId(o, clash));
            ModelState.AddModelError(nameof(model.ExternalIds), $"External ID '{clash}' is already assigned to {owner.DisplayName}.");
        }
    }

    private static string? NormalizeIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var ids = raw.Split([';', ',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var joined = string.Join(";", ids);
        return joined.Length == 0 ? null : joined;
    }

    private async Task PopulateLists(CancellationToken ct)
    {
        ViewBag.ResourceTypes = new SelectList(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name");
        ViewBag.BusinessUnits = new SelectList(await db.BusinessUnits.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name");
    }

    private static object Snapshot(Person p) =>
        new { p.DisplayName, p.ExternalIds, p.ResourceTypeId, p.BusinessUnitId, p.Seniority, p.Location, p.ResourcingClass, p.IsActive };
}
