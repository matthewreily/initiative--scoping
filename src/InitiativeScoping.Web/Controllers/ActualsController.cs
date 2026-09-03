using System.Text;
using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Actuals;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using InitiativeScoping.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Actuals: per-initiative variance page and adjustments; import runs, unmapped review and CSV upload for Finance/PMO.</summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class ActualsController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, IActualsImporter importer, TimeProvider clock, IConfiguration config) : Controller
{
    private const long MaxImportBytes = 10 * 1024 * 1024;
    // Transport cap sits above the import limit so oversize files reach the friendly redirect instead of a connection reset.
    private const long MaxImportRequestBytes = MaxImportBytes + 2 * 1024 * 1024;
    private const int PageSize = 100;

    // ----- Per-initiative -----

    [HttpGet("Initiatives/{id:int}/Actuals")]
    public async Task<IActionResult> Initiative(int id, CancellationToken ct)
    {
        var initiative = await LoadInitiativeAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        var actuals = await db.LoadActualsAsync(initiative, DefaultThreshold, ct);
        return View(new InitiativeActualsModel
        {
            Initiative = initiative,
            Variance = actuals.Variance,
            Entries = actuals.Entries,
            Adjustments = actuals.Adjustments,
            NewAdjustment = new AdjustmentEditModel { InitiativeId = id },
            CanManage = InitiativeAccess.CanManage(currentUser, initiative)
        });
    }

    [HttpPost("Initiatives/{id:int}/Adjustments")]
    public async Task<IActionResult> AddAdjustment(int id, AdjustmentEditModel model, CancellationToken ct)
    {
        var initiative = await LoadInitiativeAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanManage(currentUser, initiative))
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            return RedirectToInitiative(id, error: ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid adjustment.");
        }

        if (model.Hours == 0 && model.Cost == 0)
        {
            return RedirectToInitiative(id, error: "An adjustment must change hours or cost.");
        }

        var adjustment = new ActualAdjustment
        {
            InitiativeId = id,
            Hours = model.Hours,
            Cost = model.Cost,
            Reason = model.Reason.Trim(),
            CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow()
        };
        db.ActualAdjustments.Add(adjustment);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Initiative), id, AuditActions.Adjustment, new { adjustment.Id, adjustment.Hours, adjustment.Cost, adjustment.Reason });
        await db.SaveChangesAsync(ct);
        return RedirectToInitiative(id, success: $"Adjustment recorded ({adjustment.Hours:+0.0;-0.0;0} h, {adjustment.Cost:+$#,0;-$#,0;$0}).");
    }

    [HttpPost("Initiatives/{id:int}/Adjustments/{adjustmentId:int}/Delete")]
    public async Task<IActionResult> DeleteAdjustment(int id, int adjustmentId, CancellationToken ct)
    {
        var initiative = await LoadInitiativeAsync(id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanManage(currentUser, initiative))
        {
            return Forbid();
        }

        var adjustment = await db.ActualAdjustments.FirstOrDefaultAsync(a => a.Id == adjustmentId && a.InitiativeId == id, ct);
        if (adjustment is null)
        {
            return NotFound();
        }

        db.ActualAdjustments.Remove(adjustment);
        audit.Record(nameof(Initiative), id, AuditActions.Adjustment, new { Deleted = adjustment.Id, adjustment.Hours, adjustment.Cost, adjustment.Reason });
        await db.SaveChangesAsync(ct);
        return RedirectToInitiative(id, success: "Adjustment removed.");
    }

    // ----- Imports (Finance/PMO, Admin) -----

    [HttpGet("Actuals")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var imports = await db.ActualsImports.OrderByDescending(i => i.Id).Take(50).AsNoTracking().ToListAsync(ct);
        var unmapped = await db.ActualEntries.CountAsync(e => e.IsUnmapped, ct);
        return View(new ActualsIndexModel { Imports = imports, UnmappedCount = unmapped });
    }

    [HttpGet("Actuals/Template")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public IActionResult Template()
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", ActualsCsv.Headers));
        sb.AppendLine("PV-PROJ-100,PV-1042,2026-02-03,7.5,,TS-0001");
        sb.AppendLine("PV-PROJ-100,jane.doe@example.com,2026-02-04,8,960.00,TS-0002");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "actuals-template.csv");
    }

    [HttpPost("Actuals/Import")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    [RequestSizeLimit(MaxImportRequestBytes)]
    public async Task<IActionResult> Import(ActualsImportModel model, CancellationToken ct)
    {
        if (model.File is null || model.File.Length == 0)
        {
            return RedirectToIndex(error: "Choose a CSV file to import.");
        }

        if (model.File.Length > MaxImportBytes)
        {
            return RedirectToIndex(error: $"File exceeds the {MaxImportBytes / (1024 * 1024)} MB import limit; no changes made.");
        }

        ActualsCsvResult parsed;
        using (var reader = new StreamReader(model.File.OpenReadStream()))
        {
            parsed = ActualsCsv.Parse(reader, Path.GetFileName(model.File.FileName));
        }

        if (!parsed.IsValid)
        {
            var errors = parsed.Errors.Select(e => e.Line > 0 ? $"Line {e.Line}: {e.Message}" : e.Message).ToList();
            return RedirectToIndex(error: "Import rejected; no changes made. " + string.Join(" | ", errors.Take(10)) + (errors.Count > 10 ? $" (+{errors.Count - 10} more)" : string.Empty));
        }

        if (parsed.Rows.Count == 0)
        {
            return RedirectToIndex(error: "The file has no data rows.");
        }

        var import = await importer.ImportAsync(ActualsSources.Csv, parsed.Rows, Path.GetFileName(model.File.FileName), ct);
        TempData["Success"] = $"Imported {import.RecordCount} entries ({import.UnmappedCount} unmapped, {import.SkippedCount} skipped as duplicates).";
        return RedirectToAction(nameof(Details), new { id = import.Id });
    }

    [HttpGet("Actuals/Imports/{id:int}")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public async Task<IActionResult> Details(int id, bool unmappedOnly, int page = 1, CancellationToken ct = default)
    {
        var import = await db.ActualsImports.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (import is null)
        {
            return NotFound();
        }

        var query = db.ActualEntries.Where(e => e.ActualsImportId == id);
        if (unmappedOnly)
        {
            query = query.Where(e => e.IsUnmapped);
        }

        var model = await BuildEntriesModel(query, page, ct);
        return View(new ImportDetailsModel { Import = import, Entries = model, UnmappedOnly = unmappedOnly });
    }

    [HttpGet("Actuals/Unmapped")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public async Task<IActionResult> Unmapped(int page = 1, CancellationToken ct = default)
    {
        var model = await BuildEntriesModel(db.ActualEntries.Where(e => e.IsUnmapped), page, ct);
        return View(model);
    }

    [HttpPost("Actuals/Entries/{id:int}/Remap")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public async Task<IActionResult> Remap(int id, int? initiativeId, int? personId, string? returnUrl, CancellationToken ct)
    {
        var entry = await db.ActualEntries.Include(e => e.ActualsImport).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (entry is null)
        {
            return NotFound();
        }

        if (initiativeId is null && personId is null)
        {
            return RedirectBack(returnUrl, error: "Choose an initiative and/or a person.");
        }

        if (initiativeId is not null && !await db.Initiatives.AnyAsync(i => i.Id == initiativeId, ct))
        {
            return RedirectBack(returnUrl, error: "Unknown initiative.");
        }

        if (personId is not null && !await db.People.AnyAsync(p => p.Id == personId, ct))
        {
            return RedirectBack(returnUrl, error: "Unknown person.");
        }

        await importer.RemapAsync(entry, initiativeId, personId, ct);
        await db.SaveChangesAsync(ct);
        var state = entry.IsUnmapped ? "still unmapped" : entry.EffectiveCost is null ? "mapped but unpriced (no matching rate)" : $"mapped, {entry.EffectiveCost:C0}";
        return RedirectBack(returnUrl, success: $"Entry '{entry.SourceReference}' updated: {state}.");
    }

    [HttpPost("Actuals/Unmapped/ApplyMappings")]
    [Authorize(Policy = AppPolicies.CanManageActuals)]
    public async Task<IActionResult> ApplyMappings(string? returnUrl, CancellationToken ct)
    {
        var unmapped = await db.ActualEntries.Include(e => e.ActualsImport).Where(e => e.IsUnmapped).ToListAsync(ct);
        var mappings = await db.InitiativeSourceMappings.ToListAsync(ct);
        var people = await db.People.Where(p => p.IsActive).ToListAsync(ct);
        var changed = 0;
        foreach (var entry in unmapped)
        {
            int? initiativeId = entry.InitiativeId is null
                ? mappings.FirstOrDefault(m => string.Equals(m.Source, entry.ActualsImport!.Source, StringComparison.OrdinalIgnoreCase) && string.Equals(m.ExternalProjectId, entry.ExternalProjectId, StringComparison.OrdinalIgnoreCase))?.InitiativeId
                : null;
            int? personId = entry.PersonId is null
                ? people.FirstOrDefault(p => ActualsCosting.MatchesExternalId(p, entry.ExternalPersonId))?.Id
                : null;
            if (initiativeId is null && personId is null)
            {
                continue;
            }

            await importer.RemapAsync(entry, initiativeId, personId, ct);
            changed++;
        }

        await db.SaveChangesAsync(ct);
        return RedirectBack(returnUrl, success: $"Re-applied current mappings and roster: {changed} entr{(changed == 1 ? "y" : "ies")} updated.");
    }

    // ----- Helpers -----

    private decimal? DefaultThreshold => config.GetValue<decimal?>(ActualsQueries.DefaultThresholdKey);

    private Task<Initiative?> LoadInitiativeAsync(int id, CancellationToken ct) =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Baselines).ThenInclude(b => b.Lines)
            .Include(i => i.RebaselineRequests)
            .Include(i => i.SourceMappings)
            .AsNoTracking()
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == id, ct);

    private async Task<EntriesModel> BuildEntriesModel(IQueryable<ActualEntry> query, int page, CancellationToken ct)
    {
        page = Math.Max(1, page);
        var total = await query.CountAsync(ct);
        var entries = await query
            .Include(e => e.Initiative)
            .Include(e => e.Person)
            .Include(e => e.ActualsImport)
            .OrderByDescending(e => e.Id)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .AsNoTracking()
            .ToListAsync(ct);
        return new EntriesModel
        {
            Entries = entries,
            Total = total,
            Page = page,
            PageSize = PageSize,
            Initiatives = new SelectList(await db.Initiatives.OrderBy(i => i.Name).Select(i => new { i.Id, i.Name }).ToListAsync(ct), "Id", "Name"),
            People = new SelectList(await db.People.Where(p => p.IsActive).OrderBy(p => p.DisplayName).Select(p => new { p.Id, p.DisplayName }).ToListAsync(ct), "Id", "DisplayName")
        };
    }

    private IActionResult RedirectToInitiative(int id, string? success = null, string? error = null)
    {
        Set(success, error);
        return RedirectToAction(nameof(Initiative), new { id });
    }

    private IActionResult RedirectToIndex(string? success = null, string? error = null)
    {
        Set(success, error);
        return RedirectToAction(nameof(Index));
    }

    private IActionResult RedirectBack(string? returnUrl, string? success = null, string? error = null)
    {
        Set(success, error);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Unmapped));
    }

    private void Set(string? success, string? error)
    {
        if (success is not null)
        {
            TempData["Success"] = success;
        }

        if (error is not null)
        {
            TempData["Error"] = error;
        }
    }
}
