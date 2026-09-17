using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Exports;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using InitiativeScoping.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Cross-initiative resource demand by resource type and month against the People roster.</summary>
[Authorize(Policy = AppPolicies.CanView)]
public class CapacityController(AppDbContext db, IAuditLog audit, IEnumerable<IExportWriter> writers, IWorkCalendar workCalendar) : Controller
{
    [HttpGet("Capacity")]
    public async Task<IActionResult> Index(InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var (heatmap, calendar) = await LoadAsync(new PortfolioFilter(status, businessUnitId, includeClosed), ct);
        return View(new CapacityModel
        {
            Heatmap = heatmap,
            Status = status,
            BusinessUnitId = businessUnitId,
            IncludeClosed = includeClosed,
            HoursPerDay = calendar.HoursPerDay,
            BusinessUnits = new SelectList(await db.BusinessUnits.OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name", businessUnitId),
            Formats = writers.Select(w => w.Extension).ToList()
        });
    }

    [HttpGet("Capacity/Export")]
    public async Task<IActionResult> Export(string format, InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var writer = writers.FirstOrDefault(w => string.Equals(w.Extension, format?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var (heatmap, _) = await LoadAsync(new PortfolioFilter(status, businessUnitId, includeClosed), ct);
        var bytes = writer.Write(CapacityExport.Build(heatmap));

        audit.Record("Capacity", 0, AuditActions.Export, new { Format = writer.Extension, status, businessUnitId, includeClosed, ResourceTypes = heatmap.Rows.Count });
        await db.SaveChangesAsync(ct);

        return File(bytes, writer.ContentType, $"capacity-{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}");
    }

    private async Task<(CapacityHeatmap Heatmap, WorkCalendar Calendar)> LoadAsync(PortfolioFilter filter, CancellationToken ct)
    {
        var initiatives = await db.FilteredInitiatives(filter).ToListAsync(ct);
        var people = await db.People.AsNoTracking().ToListAsync(ct);
        var typeNames = await db.ResourceTypeNamesAsync(ct);
        var calendar = await workCalendar.GetAsync(ct);
        return (CapacityCalculator.Calculate(initiatives, people, typeNames, calendar.Holidays, calendar.HoursPerDay), calendar);
    }
}
