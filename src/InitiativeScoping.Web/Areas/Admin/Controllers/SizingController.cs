using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class SizingController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(new SizingIndexModel
        {
            Conversions = (await db.SizingConversions.ToListAsync(ct)).OrderBy(c => c.Method).ThenBy(c => c.Hours).ToList(),
            Templates = await db.AllocationTemplates.Include(t => t.Lines).ThenInclude(l => l.ResourceType)
                .OrderBy(t => t.Method).ThenBy(t => t.SizeKey).ToListAsync(ct)
        });
    }

    // ----- Conversions -----

    public IActionResult CreateConversion() => View("EditConversion", new SizingConversionEditModel());

    [HttpPost]
    public async Task<IActionResult> CreateConversion(SizingConversionEditModel model, CancellationToken ct)
    {
        await ValidateUniqueKey(model, ct);
        if (!ModelState.IsValid)
        {
            return View("EditConversion", model);
        }

        var conversion = new SizingConversion { Method = model.Method, Key = model.Key.Trim(), Hours = model.Hours };
        db.SizingConversions.Add(conversion);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(SizingConversion), conversion.Id, AuditActions.Create, new { conversion.Method, conversion.Key, conversion.Hours });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Conversion '{conversion.Key}' = {conversion.Hours}h created.");
    }

    public async Task<IActionResult> EditConversion(int id, CancellationToken ct)
    {
        var conversion = await db.SizingConversions.FindAsync([id], ct);
        if (conversion is null)
        {
            return NotFound();
        }

        return View(new SizingConversionEditModel { Id = conversion.Id, Method = conversion.Method, Key = conversion.Key, Hours = conversion.Hours });
    }

    [HttpPost]
    public async Task<IActionResult> EditConversion(int id, SizingConversionEditModel model, CancellationToken ct)
    {
        var conversion = await db.SizingConversions.FindAsync([id], ct);
        if (conversion is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueKey(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { conversion.Method, conversion.Key, conversion.Hours };
        conversion.Method = model.Method;
        conversion.Key = model.Key.Trim();
        conversion.Hours = model.Hours;
        audit.Record(nameof(SizingConversion), conversion.Id, AuditActions.Update, new { Before = before, After = new { conversion.Method, conversion.Key, conversion.Hours } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Conversion '{conversion.Key}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteConversion(int id, CancellationToken ct)
    {
        var conversion = await db.SizingConversions.FindAsync([id], ct);
        if (conversion is null)
        {
            return NotFound();
        }

        db.SizingConversions.Remove(conversion);
        audit.Record(nameof(SizingConversion), conversion.Id, AuditActions.Delete, new { conversion.Method, conversion.Key, conversion.Hours });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Conversion '{conversion.Key}' deleted.");
    }

    // ----- Allocation templates -----

    public async Task<IActionResult> CreateTemplate(CancellationToken ct)
    {
        await PopulateResourceTypes(ct);
        return View("EditTemplate", new AllocationTemplateEditModel { Lines = [new AllocationTemplateLineEditModel()] });
    }

    [HttpPost]
    public async Task<IActionResult> CreateTemplate(AllocationTemplateEditModel model, CancellationToken ct)
    {
        NormalizeLines(model);
        await ValidateTemplate(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateResourceTypes(ct);
            return View("EditTemplate", model);
        }

        var template = new AllocationTemplate { Method = model.Method, SizeKey = model.SizeKey.Trim(), Name = model.Name.Trim() };
        template.Lines.AddRange(model.Lines.Select(ToEntity));
        db.AllocationTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(AllocationTemplate), template.Id, AuditActions.Create, new { template.Method, template.SizeKey, template.Name, LineCount = template.Lines.Count });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Template '{template.Name}' created.");
    }

    public async Task<IActionResult> EditTemplate(int id, CancellationToken ct)
    {
        var template = await db.AllocationTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return NotFound();
        }

        await PopulateResourceTypes(ct);
        return View(new AllocationTemplateEditModel
        {
            Id = template.Id,
            Method = template.Method,
            SizeKey = template.SizeKey,
            Name = template.Name,
            Lines = template.Lines.Select(l => new AllocationTemplateLineEditModel
            {
                PhaseName = l.PhaseName, ResourceTypeId = l.ResourceTypeId, Seniority = l.Seniority, Percent = l.Percent
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditTemplate(int id, AllocationTemplateEditModel model, CancellationToken ct)
    {
        var template = await db.AllocationTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return NotFound();
        }

        model.Id = id;
        NormalizeLines(model);
        await ValidateTemplate(model, ct);
        if (!ModelState.IsValid)
        {
            await PopulateResourceTypes(ct);
            return View(model);
        }

        var before = new { template.Method, template.SizeKey, template.Name, LineCount = template.Lines.Count };
        template.Method = model.Method;
        template.SizeKey = model.SizeKey.Trim();
        template.Name = model.Name.Trim();
        db.AllocationTemplateLines.RemoveRange(template.Lines);
        template.Lines = model.Lines.Select(ToEntity).ToList();
        audit.Record(nameof(AllocationTemplate), template.Id, AuditActions.Update, new { Before = before, After = new { template.Method, template.SizeKey, template.Name, LineCount = template.Lines.Count } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Template '{template.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct)
    {
        var template = await db.AllocationTemplates.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
        {
            return NotFound();
        }

        db.AllocationTemplates.Remove(template);
        audit.Record(nameof(AllocationTemplate), template.Id, AuditActions.Delete, new { template.Method, template.SizeKey, template.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Template '{template.Name}' deleted.");
    }

    private static AllocationTemplateLine ToEntity(AllocationTemplateLineEditModel l) => new()
    {
        PhaseName = l.PhaseName.Trim(), ResourceTypeId = l.ResourceTypeId, Seniority = l.Seniority, Percent = l.Percent
    };

    private static void NormalizeLines(AllocationTemplateEditModel model)
    {
        model.Lines = model.Lines
            .Where(l => !string.IsNullOrWhiteSpace(l.PhaseName) || l.ResourceTypeId != 0 || l.Percent != 0)
            .ToList();
    }

    private async Task ValidateTemplate(AllocationTemplateEditModel model, CancellationToken ct)
    {
        var key = model.SizeKey.Trim();
        if (await db.AllocationTemplates.AnyAsync(t => t.Id != model.Id && t.Method == model.Method && t.SizeKey == key, ct))
        {
            ModelState.AddModelError(nameof(model.SizeKey), "A template for this method and size already exists.");
        }

        if (model.Lines.Count == 0)
        {
            ModelState.AddModelError(nameof(model.Lines), "Add at least one line.");
        }
        else if (Math.Abs(model.Lines.Sum(l => l.Percent) - 100m) > 0.01m)
        {
            ModelState.AddModelError(nameof(model.Lines), $"Line percentages must total 100% (currently {model.Lines.Sum(l => l.Percent):0.##}%).");
        }
    }

    private async Task ValidateUniqueKey(SizingConversionEditModel model, CancellationToken ct)
    {
        var key = model.Key.Trim();
        if (await db.SizingConversions.AnyAsync(c => c.Id != model.Id && c.Method == model.Method && c.Key == key, ct))
        {
            ModelState.AddModelError(nameof(model.Key), "A conversion for this method and key already exists.");
        }
    }

    private async Task PopulateResourceTypes(CancellationToken ct)
    {
        ViewBag.ResourceTypes = new SelectList(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name");
    }
}
