using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Exports;
using InitiativeScoping.Domain.Entities;
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

/// <summary>Portfolio dashboard (all initiatives, forecast vs. baseline vs. spent) and CSV/XLSX exports.</summary>
[Authorize(Policy = AppPolicies.CanView)]
public class PortfolioController(AppDbContext db, IAuditLog audit, IEnumerable<IExportWriter> writers, IConfiguration config, IWorkCalendar workCalendar) : Controller
{
    [HttpGet("Portfolio")]
    public async Task<IActionResult> Index(InitiativeStatus? status, int? businessUnitId, bool includeClosed, string? sort, string? dir, int page = 1, int? size = null, CancellationToken ct = default)
    {
        var portfolio = await db.LoadPortfolioAsync(new PortfolioFilter(status, businessUnitId, includeClosed), DefaultThreshold, ct);
        var sortKey = PortfolioSort.Normalize(sort);
        var desc = string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var pageSize = Paging.NormalizeSize(size, PortfolioSort.DefaultPageSize);
        page = Paging.ClampPage(page, portfolio.Count, pageSize);
        var pageRows = PortfolioSort.Apply(portfolio.Rows, sortKey, desc).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return View(new PortfolioModel
        {
            Portfolio = portfolio,
            PageRows = pageRows,
            Page = page,
            PageSize = pageSize,
            Sort = sortKey,
            Desc = desc,
            Status = status,
            BusinessUnitId = businessUnitId,
            IncludeClosed = includeClosed,
            BusinessUnits = new SelectList(await db.BusinessUnits.OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name", businessUnitId),
            CanExport = true,
            Formats = writers.Select(w => w.Extension).ToList(),
            Fiscal = (await workCalendar.GetAsync(ct)).Fiscal
        });
    }

    [HttpGet("Portfolio/Export")]
    [Authorize(Policy = AppPolicies.CanView)]
    public async Task<IActionResult> Export(string format, InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var writer = ResolveWriter(format);
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var portfolio = await db.LoadPortfolioAsync(new PortfolioFilter(status, businessUnitId, includeClosed), DefaultThreshold, ct);
        var bytes = writer.Write(PortfolioExport.Build(portfolio, (await workCalendar.GetAsync(ct)).Fiscal));

        audit.Record("Portfolio", 0, AuditActions.Export, new { Format = writer.Extension, status, businessUnitId, includeClosed, Initiatives = portfolio.Count });
        await db.SaveChangesAsync(ct);

        return File(bytes, writer.ContentType, $"portfolio-{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}");
    }

    [HttpGet("Initiatives/{id:int}/Export")]
    [Authorize(Policy = AppPolicies.CanView)]
    public async Task<IActionResult> ExportInitiative(int id, string format, CancellationToken ct)
    {
        var writer = ResolveWriter(format);
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var initiative = await db.PortfolioInitiatives().FirstOrDefaultAsync(i => i.Id == id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        var cards = await db.PricingRateCardsAsync(ct);
        var forecast = ForecastCalculator.Calculate(initiative, cards);
        var actuals = await db.LoadActualsAsync(initiative, DefaultThreshold, ct);
        var typeNames = await db.ResourceTypeNamesAsync(ct);
        var businessUnitNames = await db.BusinessUnits.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var vendorNames = await db.Vendors.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Name, ct);
        var seniorityNames = await db.SeniorityLevels.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var phasing = MonthlyPhasingCalculator.Calculate(initiative, forecast, initiative.CurrentBaseline, actuals.Entries, actuals.Adjustments);
        var bytes = writer.Write(InitiativeExport.Build(initiative, forecast, actuals.Variance, actuals.Entries, actuals.Adjustments, phasing, typeNames, businessUnitNames, vendorNames, seniorityNames, (await workCalendar.GetAsync(ct)).Fiscal));

        audit.Record(nameof(Initiative), id, AuditActions.Export, new { Format = writer.Extension, Rows = actuals.Entries.Count });
        await db.SaveChangesAsync(ct);

        return File(bytes, writer.ContentType, $"initiative-{id}-{ExportFormats.SafeFileName(initiative.Name)}.{writer.Extension}");
    }

    private IExportWriter? ResolveWriter(string? format) =>
        writers.FirstOrDefault(w => string.Equals(w.Extension, format?.Trim(), StringComparison.OrdinalIgnoreCase));

    private decimal? DefaultThreshold => config.GetValue<decimal?>(ActualsQueries.DefaultThresholdKey);
}
