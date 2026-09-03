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
public class PortfolioController(AppDbContext db, IAuditLog audit, IEnumerable<IExportWriter> writers, IConfiguration config) : Controller
{
    [HttpGet("Portfolio")]
    public async Task<IActionResult> Index(InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var portfolio = await LoadPortfolioAsync(status, businessUnitId, includeClosed, ct);
        return View(new PortfolioModel
        {
            Portfolio = portfolio,
            Status = status,
            BusinessUnitId = businessUnitId,
            IncludeClosed = includeClosed,
            BusinessUnits = new SelectList(await db.BusinessUnits.OrderBy(b => b.Name).ToListAsync(ct), "Id", "Name", businessUnitId),
            CanExport = User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.FinancePmo),
            Formats = writers.Select(w => w.Extension).ToList()
        });
    }

    [HttpGet("Portfolio/Export")]
    [Authorize(Policy = AppPolicies.CanExport)]
    public async Task<IActionResult> Export(string format, InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var writer = ResolveWriter(format);
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var portfolio = await LoadPortfolioAsync(status, businessUnitId, includeClosed, ct);
        var bytes = writer.Write(PortfolioExport.Build(portfolio));

        audit.Record("Portfolio", 0, AuditActions.Export, new { Format = writer.Extension, status, businessUnitId, includeClosed, Initiatives = portfolio.Count });
        await db.SaveChangesAsync(ct);

        return File(bytes, writer.ContentType, $"portfolio-{DateTime.UtcNow:yyyyMMdd}.{writer.Extension}");
    }

    [HttpGet("Initiatives/{id:int}/Export")]
    [Authorize(Policy = AppPolicies.CanExport)]
    public async Task<IActionResult> ExportInitiative(int id, string format, CancellationToken ct)
    {
        var writer = ResolveWriter(format);
        if (writer is null)
        {
            return BadRequest($"Unsupported format '{format}'. Use one of: {string.Join(", ", writers.Select(w => w.Extension))}.");
        }

        var initiative = await InitiativeQuery()
            .Include(i => i.Allocations)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (initiative is null)
        {
            return NotFound();
        }

        var cards = await LoadRateCardsAsync(ct);
        var forecast = ForecastCalculator.Calculate(initiative, cards);
        var actuals = await db.LoadActualsAsync(initiative, DefaultThreshold, ct);
        var typeNames = await db.ResourceTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var bytes = writer.Write(InitiativeExport.Build(initiative, forecast, actuals.Variance, actuals.Entries, actuals.Adjustments, typeNames));

        audit.Record(nameof(Initiative), id, AuditActions.Export, new { Format = writer.Extension, Rows = actuals.Entries.Count });
        await db.SaveChangesAsync(ct);

        return File(bytes, writer.ContentType, $"initiative-{id}-{ExportFormats.SafeFileName(initiative.Name)}.{writer.Extension}");
    }

    private IExportWriter? ResolveWriter(string? format) =>
        writers.FirstOrDefault(w => string.Equals(w.Extension, format?.Trim(), StringComparison.OrdinalIgnoreCase));

    private decimal? DefaultThreshold => config.GetValue<decimal?>(ActualsQueries.DefaultThresholdKey);

    private async Task<PortfolioResult> LoadPortfolioAsync(InitiativeStatus? status, int? businessUnitId, bool includeClosed, CancellationToken ct)
    {
        var query = InitiativeQuery().Include(i => i.Allocations).AsQueryable();
        if (status is not null)
        {
            query = query.Where(i => i.Status == status);
        }
        else if (!includeClosed)
        {
            query = query.Where(i => i.Status != InitiativeStatus.Complete && i.Status != InitiativeStatus.Cancelled);
        }

        if (businessUnitId is not null)
        {
            query = query.Where(i => i.BusinessUnitId == businessUnitId);
        }

        var initiatives = await query.OrderBy(i => i.BusinessUnit!.Name).ThenBy(i => i.Name).ToListAsync(ct);
        var ids = initiatives.Select(i => i.Id).ToList();
        var entries = await db.ActualEntries
            .Include(e => e.Person)
            .Where(e => e.InitiativeId != null && ids.Contains(e.InitiativeId.Value) && !e.IsUnmapped)
            .AsNoTracking()
            .ToListAsync(ct);
        var adjustments = await db.ActualAdjustments.Where(a => ids.Contains(a.InitiativeId)).AsNoTracking().ToListAsync(ct);
        var typeNames = await db.ResourceTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var cards = await LoadRateCardsAsync(ct);

        return PortfolioCalculator.Calculate(initiatives, cards, entries, adjustments, typeNames, DefaultThreshold);
    }

    private IQueryable<Initiative> InitiativeQuery() =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Baselines).ThenInclude(b => b.Lines)
            .Include(i => i.RebaselineRequests)
            .AsNoTracking();

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) =>
        db.RateCards.Include(c => c.Entries).Where(c => c.Status == RateCardStatus.Published).AsNoTracking().ToListAsync(ct);
}
