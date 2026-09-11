using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class SenioritiesController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var items = await db.SeniorityLevels
            .OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new SeniorityListItem
            {
                Level = s,
                RateCardEntries = db.RateCardEntries.Count(e => e.SeniorityId == s.Id),
                Allocations = db.InitiativeAllocations.Count(a => a.SeniorityId == s.Id),
                People = db.People.Count(p => p.SeniorityId == s.Id),
                TemplateLines = db.AllocationTemplateLines.Count(l => l.SeniorityId == s.Id)
            })
            .ToListAsync(ct);
        return View(items);
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var next = await db.SeniorityLevels.Select(s => (int?)s.SortOrder).MaxAsync(ct) ?? 0;
        return View("Edit", new SeniorityEditModel { SortOrder = next + 1 });
    }

    [HttpPost]
    public async Task<IActionResult> Create(SeniorityEditModel model, CancellationToken ct)
    {
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var level = new SeniorityLevel { Name = model.Name.Trim(), SortOrder = model.SortOrder, IsActive = model.IsActive };
        db.SeniorityLevels.Add(level);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(SeniorityLevel), level.Id, AuditActions.Create, new { level.Name, level.SortOrder });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Seniority level '{level.Name}' created.");
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var level = await db.SeniorityLevels.FindAsync([id], ct);
        if (level is null)
        {
            return NotFound();
        }

        return View(new SeniorityEditModel { Id = level.Id, Name = level.Name, SortOrder = level.SortOrder, IsActive = level.IsActive });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, SeniorityEditModel model, CancellationToken ct)
    {
        var level = await db.SeniorityLevels.FindAsync([id], ct);
        if (level is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueName(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { level.Name, level.SortOrder, level.IsActive };
        level.Name = model.Name.Trim();
        level.SortOrder = model.SortOrder;
        level.IsActive = model.IsActive;
        audit.Record(nameof(SeniorityLevel), level.Id, AuditActions.Update, new { Before = before, After = new { level.Name, level.SortOrder, level.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Seniority level '{level.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var level = await db.SeniorityLevels.FindAsync([id], ct);
        if (level is null)
        {
            return NotFound();
        }

        if (await IsReferencedAsync(level, ct))
        {
            return RedirectWithError($"'{level.Name}' is referenced by rate cards, allocations, people or templates and cannot be deleted. Deactivate it instead.");
        }

        db.SeniorityLevels.Remove(level);
        audit.Record(nameof(SeniorityLevel), level.Id, AuditActions.Delete, new { level.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Seniority level '{level.Name}' deleted.");
    }

    [HttpPost]
    public Task<IActionResult> BulkDelete(int[] ids, CancellationToken ct) => BulkDeleteRows(
        db, db.SeniorityLevels, ids, x => s => x.Contains(s.Id),
        async (s, c) => !await IsReferencedAsync(s, c),
        s => s.Name,
        s => audit.Record(nameof(SeniorityLevel), s.Id, AuditActions.Delete, new { s.Name }),
        "seniority level", "seniority levels", "referenced; deactivate instead", ct);

    [HttpPost]
    public Task<IActionResult> BulkActivate(int[] ids, CancellationToken ct) => SetActive(ids, true, ct);

    [HttpPost]
    public Task<IActionResult> BulkDeactivate(int[] ids, CancellationToken ct) => SetActive(ids, false, ct);

    private Task<IActionResult> SetActive(int[] ids, bool active, CancellationToken ct) => BulkSetActive(
        db, db.SeniorityLevels, ids, x => s => x.Contains(s.Id),
        s => s.IsActive, (s, a) => s.IsActive = a,
        (s, a) => audit.Record(nameof(SeniorityLevel), s.Id, AuditActions.Update, new { Before = new { IsActive = !a }, After = new { IsActive = a } }),
        active, "seniority level", "seniority levels", ct);

    private async Task<bool> IsReferencedAsync(SeniorityLevel level, CancellationToken ct) =>
        await db.RateCardEntries.AnyAsync(e => e.SeniorityId == level.Id, ct)
        || await db.InitiativeAllocations.AnyAsync(a => a.SeniorityId == level.Id, ct)
        || await db.People.AnyAsync(p => p.SeniorityId == level.Id, ct)
        || await db.AllocationTemplateLines.AnyAsync(l => l.SeniorityId == level.Id, ct)
        || await db.ForecastBaselineLines.AnyAsync(l => l.SeniorityId == level.Id, ct);

    private async Task ValidateUniqueName(SeniorityEditModel model, CancellationToken ct)
    {
        var name = model.Name.Trim().ToLowerInvariant();
        if (await db.SeniorityLevels.AnyAsync(s => s.Id != model.Id && s.Name.ToLower() == name, ct))
        {
            ModelState.AddModelError(nameof(model.Name), "A seniority level with this name already exists.");
        }
    }
}
