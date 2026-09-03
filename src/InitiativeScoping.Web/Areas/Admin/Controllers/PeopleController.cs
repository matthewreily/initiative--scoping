using System.Text;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.People;
using InitiativeScoping.Domain.Enums;
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
    private const long MaxImportBytes = 5 * 1024 * 1024;
    private const long MaxImportRequestBytes = MaxImportBytes + 2 * 1024 * 1024;

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

    public async Task<IActionResult> Export(CancellationToken ct)
    {
        var people = await db.People.Include(p => p.ResourceType).Include(p => p.BusinessUnit)
            .OrderBy(p => p.DisplayName).ToListAsync(ct);
        var rows = people.Select(p => new PeopleCsvRow(p.DisplayName, SplitIds(p.ExternalIds), p.ResourceType!.Name, p.BusinessUnit!.Name,
            p.Seniority, p.Location, p.ResourcingClass, p.IsActive));
        return Csv(rows, "people.csv");
    }

    public IActionResult Template() =>
        Csv(
        [
            new PeopleCsvRow("Jane Doe", ["PV-1001", "jane.doe@example.com"], "Software Engineer", "Boarding", Seniority.Senior, "Onshore", ResourcingClass.InternalFte, true),
            new PeopleCsvRow("Vendor Dev 1", ["VND-77"], "Software Engineer", "Boarding", Seniority.Mid, "Offshore", ResourcingClass.Vendor, true)
        ], "people-template.csv");

    /// <summary>
    /// Upserts people from CSV. A row matches an existing person by any shared external ID, otherwise by display name
    /// (case-insensitive). The whole file is rejected if any row is invalid or references unknown reference data.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxImportRequestBytes)]
    public async Task<IActionResult> Import(PeopleImportModel model, CancellationToken ct)
    {
        if (model.File is null || model.File.Length == 0)
        {
            return RedirectWithError("Choose a CSV file to import.");
        }

        if (model.File.Length > MaxImportBytes)
        {
            return RedirectWithError($"File exceeds the {MaxImportBytes / (1024 * 1024)} MB import limit; no changes made.");
        }

        PeopleCsvResult parsed;
        using (var reader = new StreamReader(model.File.OpenReadStream()))
        {
            parsed = PeopleCsv.Parse(reader);
        }

        var resourceTypes = await db.ResourceTypes.ToDictionaryAsync(t => t.Name, t => t.Id, StringComparer.OrdinalIgnoreCase, ct);
        var businessUnits = await db.BusinessUnits.ToDictionaryAsync(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase, ct);

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

        var people = await db.People.ToListAsync(ct);
        var matches = new Dictionary<PeopleCsvRow, Person?>();
        foreach (var row in parsed.Rows)
        {
            var byId = people.Where(p => row.ExternalIds.Any(id => ActualsCosting.MatchesExternalId(p, id))).Distinct().ToList();
            if (byId.Count > 1)
            {
                errors.Add($"'{row.DisplayName}': external IDs match more than one existing person ({string.Join(", ", byId.Select(p => p.DisplayName))}).");
                continue;
            }

            var byName = people.FirstOrDefault(p => string.Equals(p.DisplayName, row.DisplayName, StringComparison.OrdinalIgnoreCase));
            var match = byId.SingleOrDefault() ?? byName;
            if (byId.Count == 1 && byName is not null && byName != byId[0])
            {
                errors.Add($"'{row.DisplayName}': external IDs belong to '{byId[0].DisplayName}' but the name matches another person.");
                continue;
            }

            if (match is not null && matches.ContainsValue(match))
            {
                errors.Add($"'{row.DisplayName}': more than one row resolves to existing person '{match.DisplayName}'.");
                continue;
            }

            matches[row] = match;
        }

        if (errors.Count > 0)
        {
            return RedirectWithError("Import rejected; no changes made. " + string.Join(" | ", errors.Take(10)) + (errors.Count > 10 ? $" (+{errors.Count - 10} more)" : string.Empty));
        }

        var added = 0;
        var updated = 0;
        foreach (var (row, existing) in matches)
        {
            var ids = row.ExternalIds.Count == 0 ? null : string.Join(";", row.ExternalIds);
            if (existing is null)
            {
                var person = new Person
                {
                    DisplayName = row.DisplayName, ExternalIds = ids, ResourceTypeId = resourceTypes[row.ResourceType], BusinessUnitId = businessUnits[row.BusinessUnit],
                    Seniority = row.Seniority, Location = row.Location, ResourcingClass = row.ResourcingClass, IsActive = row.IsActive
                };
                db.People.Add(person);
                people.Add(person);
                added++;
                continue;
            }

            var before = Snapshot(existing);
            existing.DisplayName = row.DisplayName;
            existing.ExternalIds = ids;
            existing.ResourceTypeId = resourceTypes[row.ResourceType];
            existing.BusinessUnitId = businessUnits[row.BusinessUnit];
            existing.Seniority = row.Seniority;
            existing.Location = row.Location;
            existing.ResourcingClass = row.ResourcingClass;
            existing.IsActive = row.IsActive;
            if (db.Entry(existing).State == EntityState.Modified)
            {
                audit.Record(nameof(Person), existing.Id, AuditActions.Update, new { Before = before, After = Snapshot(existing) });
                updated++;
            }
        }

        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Person), 0, AuditActions.Import, new { model.File.FileName, Added = added, Updated = updated });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Import complete: {added} added, {updated} updated. Existing actuals keep their calculated cost.");
    }

    private FileContentResult Csv(IEnumerable<PeopleCsvRow> rows, string fileName)
    {
        var sb = new StringBuilder();
        using (var writer = new StringWriter(sb))
        {
            PeopleCsv.Write(writer, rows);
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", fileName);
    }

    private static IReadOnlyList<string> SplitIds(string? ids) =>
        (ids ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

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
