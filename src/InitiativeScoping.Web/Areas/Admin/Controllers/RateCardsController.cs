using System.Text;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.RateCards;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class RateCardsController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var cards = await db.RateCards
            .Include(c => c.Entries)
            .OrderByDescending(c => c.EffectiveStart).ThenBy(c => c.Name)
            .ToListAsync(ct);
        return View(cards);
    }

    public IActionResult Create() => View("Edit", new RateCardEditModel());

    [HttpPost]
    public async Task<IActionResult> Create(RateCardEditModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View("Edit", model);
        }

        var card = new RateCard { Name = model.Name.Trim(), EffectiveStart = model.EffectiveStart };
        db.RateCards.Add(card);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(RateCard), card.Id, AuditActions.Create, new { card.Name, card.EffectiveStart });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Rate card '{card.Name}' created. Add entries, then publish.", "Details", new { id = card.Id });
    }

    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var card = await db.RateCards.FindAsync([id], ct);
        if (card is null)
        {
            return NotFound();
        }

        return View(new RateCardEditModel { Id = card.Id, Name = card.Name, EffectiveStart = card.EffectiveStart });
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, RateCardEditModel model, CancellationToken ct)
    {
        var card = await db.RateCards.FindAsync([id], ct);
        if (card is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            model.Id = id;
            return View(model);
        }

        var before = new { card.Name, card.EffectiveStart };
        card.Name = model.Name.Trim();
        card.EffectiveStart = model.EffectiveStart;
        audit.Record(nameof(RateCard), card.Id, AuditActions.Update, new { Before = before, After = new { card.Name, card.EffectiveStart } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Rate card '{card.Name}' updated.", "Details", new { id });
    }

    public async Task<IActionResult> Details(int id, string? resourceType, CancellationToken ct)
    {
        var card = await db.RateCards
            .Include(c => c.Entries).ThenInclude(e => e.ResourceType)
            .Include(c => c.Entries).ThenInclude(e => e.BusinessUnit)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null)
        {
            return NotFound();
        }

        card.Entries = card.Entries
            .Where(e => string.IsNullOrEmpty(resourceType) || e.ResourceType!.Name == resourceType)
            .OrderBy(e => e.ResourceType!.Name).ThenBy(e => e.BusinessUnit!.Name)
            .ThenBy(e => e.ResourcingClass).ThenBy(e => e.Location).ThenBy(e => e.Seniority)
            .ToList();

        return View(new RateCardDetailsModel
        {
            Card = card,
            NewEntry = new RateCardEntryEditModel { RateCardId = id },
            ResourceTypes = await ResourceTypeSelect(ct),
            BusinessUnits = await BusinessUnitSelect(ct),
            FilterResourceType = resourceType
        });
    }

    [HttpPost]
    public async Task<IActionResult> AddEntry(int id, RateCardEntryEditModel model, CancellationToken ct)
    {
        var card = await db.RateCards.FindAsync([id], ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status == RateCardStatus.Retired)
        {
            return RedirectWithError("Retired rate cards cannot be edited.", "Details", new { id });
        }

        var location = model.Location.Trim();
        var duplicate = await db.RateCardEntries.AnyAsync(e =>
            e.RateCardId == id && e.ResourceTypeId == model.ResourceTypeId && e.BusinessUnitId == model.BusinessUnitId &&
            e.Seniority == model.Seniority && e.ResourcingClass == model.ResourcingClass && e.Location == location, ct);
        if (duplicate)
        {
            return RedirectWithError("An entry with the same resource type / business unit / seniority / location / class already exists.", "Details", new { id });
        }

        if (!ModelState.IsValid)
        {
            return RedirectWithError("Entry is invalid: " + string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)), "Details", new { id });
        }

        var entry = new RateCardEntry
        {
            RateCardId = id,
            ResourceTypeId = model.ResourceTypeId,
            BusinessUnitId = model.BusinessUnitId,
            Seniority = model.Seniority,
            Location = location,
            ResourcingClass = model.ResourcingClass,
            HourlyRate = model.HourlyRate
        };
        db.RateCardEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(RateCardEntry), entry.Id, AuditActions.Create, new { entry.RateCardId, entry.ResourceTypeId, entry.BusinessUnitId, entry.Seniority, entry.Location, entry.ResourcingClass, entry.HourlyRate });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Entry added.", "Details", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> UpdateEntry(int id, int entryId, decimal hourlyRate, CancellationToken ct)
    {
        var entry = await db.RateCardEntries.Include(e => e.RateCard).FirstOrDefaultAsync(e => e.Id == entryId && e.RateCardId == id, ct);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.RateCard!.Status == RateCardStatus.Retired)
        {
            return RedirectWithError("Retired rate cards cannot be edited.", "Details", new { id });
        }

        if (hourlyRate < 0)
        {
            return RedirectWithError("Hourly rate must be zero or greater.", "Details", new { id });
        }

        var before = entry.HourlyRate;
        entry.HourlyRate = hourlyRate;
        audit.Record(nameof(RateCardEntry), entry.Id, AuditActions.Update, new { Before = before, After = hourlyRate });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Rate updated.", "Details", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteEntry(int id, int entryId, CancellationToken ct)
    {
        var entry = await db.RateCardEntries.Include(e => e.RateCard).FirstOrDefaultAsync(e => e.Id == entryId && e.RateCardId == id, ct);
        if (entry is null)
        {
            return NotFound();
        }

        if (entry.RateCard!.Status == RateCardStatus.Retired)
        {
            return RedirectWithError("Retired rate cards cannot be edited.", "Details", new { id });
        }

        db.RateCardEntries.Remove(entry);
        audit.Record(nameof(RateCardEntry), entry.Id, AuditActions.Delete, new { entry.ResourceTypeId, entry.BusinessUnitId, entry.Seniority, entry.Location, entry.ResourcingClass, entry.HourlyRate });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Entry removed.", "Details", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Publish(int id, CancellationToken ct)
    {
        var card = await db.RateCards.Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status != RateCardStatus.Draft)
        {
            return RedirectWithError("Only draft rate cards can be published.", "Details", new { id });
        }

        if (card.Entries.Count == 0)
        {
            return RedirectWithError("Add at least one entry before publishing.", "Details", new { id });
        }

        card.Status = RateCardStatus.Published;
        audit.Record(nameof(RateCard), card.Id, AuditActions.Publish, new { card.Name, card.EffectiveStart, EntryCount = card.Entries.Count });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Rate card '{card.Name}' published.", "Details", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Retire(int id, CancellationToken ct)
    {
        var card = await db.RateCards.FindAsync([id], ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status != RateCardStatus.Published)
        {
            return RedirectWithError("Only published rate cards can be retired.", "Details", new { id });
        }

        card.Status = RateCardStatus.Retired;
        audit.Record(nameof(RateCard), card.Id, AuditActions.Retire, new { card.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Rate card '{card.Name}' retired. Existing baselines keep their snapshotted rates.", "Details", new { id });
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var card = await db.RateCards.Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status != RateCardStatus.Draft)
        {
            return RedirectWithError("Only draft rate cards can be deleted. Retire published cards instead so historical forecasts remain reproducible.");
        }

        db.RateCards.Remove(card);
        audit.Record(nameof(RateCard), card.Id, AuditActions.Delete, new { card.Name, EntryCount = card.Entries.Count });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Rate card '{card.Name}' deleted.");
    }

    public async Task<IActionResult> Export(int id, CancellationToken ct)
    {
        var card = await db.RateCards
            .Include(c => c.Entries).ThenInclude(e => e.ResourceType)
            .Include(c => c.Entries).ThenInclude(e => e.BusinessUnit)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null)
        {
            return NotFound();
        }

        var rows = card.Entries
            .OrderBy(e => e.ResourceType!.Name).ThenBy(e => e.BusinessUnit!.Name).ThenBy(e => e.Seniority)
            .Select(e => new RateCardCsvRow(e.ResourceType!.Name, e.BusinessUnit!.Name, e.Seniority, e.Location, e.ResourcingClass, e.HourlyRate));

        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            RateCardCsv.Write(writer, rows);
        }

        var safeName = string.Concat(card.Name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'));
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"ratecard-{safeName}.csv");
    }

    public IActionResult Template()
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            RateCardCsv.Write(writer,
            [
                new RateCardCsvRow("Software Engineer", "Boarding", Seniority.Senior, "Onshore", ResourcingClass.InternalFte, 120m),
                new RateCardCsvRow("Software Engineer", "Boarding", Seniority.Senior, "Offshore", ResourcingClass.Vendor, 75m)
            ]);
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "ratecard-template.csv");
    }

    [HttpPost]
    [RequestSizeLimit(MaxImportBytes)]
    public async Task<IActionResult> Import(int id, RateCardImportModel model, CancellationToken ct)
    {
        var card = await db.RateCards.Include(c => c.Entries).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (card is null)
        {
            return NotFound();
        }

        if (card.Status == RateCardStatus.Retired)
        {
            return RedirectWithError("Retired rate cards cannot be edited.", "Details", new { id });
        }

        if (model.File is null || model.File.Length == 0)
        {
            return RedirectWithError("Choose a CSV file to import.", "Details", new { id });
        }

        RateCardCsvResult parsed;
        using (var reader = new StreamReader(model.File.OpenReadStream()))
        {
            parsed = RateCardCsv.Parse(reader);
        }

        var resourceTypes = ToLookup(await db.ResourceTypes.Select(t => new { t.Name, t.Id }).ToListAsync(ct), x => x.Name, x => x.Id);
        var businessUnits = ToLookup(await db.BusinessUnits.Select(b => new { b.Name, b.Id }).ToListAsync(ct), x => x.Name, x => x.Id);

        var errors = parsed.Errors.Select(e => e.Line > 0 ? $"Line {e.Line}: {e.Message}" : e.Message).ToList();
        var unknownTypes = parsed.Rows.Select(r => r.ResourceType).Where(n => !resourceTypes.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unknownUnits = parsed.Rows.Select(r => r.BusinessUnit).Where(n => !businessUnits.ContainsKey(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (unknownTypes.Count > 0)
        {
            errors.Add("Unknown resource type(s): " + string.Join(", ", unknownTypes));
        }
        if (unknownUnits.Count > 0)
        {
            errors.Add("Unknown business unit(s): " + string.Join(", ", unknownUnits));
        }

        if (errors.Count > 0)
        {
            return RedirectWithError("Import rejected; no changes made. " + string.Join(" | ", errors.Take(10)) + (errors.Count > 10 ? $" (+{errors.Count - 10} more)" : string.Empty), "Details", new { id });
        }

        var removed = 0;
        if (model.Replace)
        {
            removed = card.Entries.Count;
            db.RateCardEntries.RemoveRange(card.Entries);
            card.Entries.Clear();
        }

        var added = 0;
        var updated = 0;
        foreach (var row in parsed.Rows)
        {
            var typeId = resourceTypes[row.ResourceType];
            var unitId = businessUnits[row.BusinessUnit];
            var existing = card.Entries.FirstOrDefault(e =>
                e.ResourceTypeId == typeId && e.BusinessUnitId == unitId && e.Seniority == row.Seniority &&
                e.ResourcingClass == row.ResourcingClass && string.Equals(e.Location, row.Location, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                card.Entries.Add(new RateCardEntry
                {
                    ResourceTypeId = typeId, BusinessUnitId = unitId, Seniority = row.Seniority,
                    Location = row.Location, ResourcingClass = row.ResourcingClass, HourlyRate = row.HourlyRate
                });
                added++;
            }
            else if (existing.HourlyRate != row.HourlyRate)
            {
                existing.HourlyRate = row.HourlyRate;
                updated++;
            }
        }

        audit.Record(nameof(RateCard), card.Id, AuditActions.Import, new { model.File.FileName, model.Replace, Added = added, Updated = updated, Removed = removed });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Import complete: {added} added, {updated} updated, {removed} removed.", "Details", new { id });
    }

    private static Dictionary<string, int> ToLookup<T>(IEnumerable<T> items, Func<T, string> name, Func<T, int> id) =>
        items.GroupBy(name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => id(g.First()), StringComparer.OrdinalIgnoreCase);

    private async Task<SelectList> ResourceTypeSelect(CancellationToken ct) =>
        new(await db.ResourceTypes.Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct), "Id", "Name");

    private async Task<SelectList> BusinessUnitSelect(CancellationToken ct) =>
        new(await db.BusinessUnits.Where(b => b.IsActive).OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name");
}
