using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Services;

public sealed record PortfolioFilter(InitiativeStatus? Status, int? BusinessUnitId, bool IncludeClosed);

/// <summary>Read-only loaders shared by the portfolio dashboard and exports (no change tracking, split queries).</summary>
public static class PortfolioQueries
{
    public static async Task<PortfolioResult> LoadPortfolioAsync(this AppDbContext db, PortfolioFilter filter, decimal? defaultThresholdPct, CancellationToken ct)
    {
        var initiatives = await db.FilteredInitiatives(filter).ToListAsync(ct);
        var ids = initiatives.Select(i => i.Id).ToList();
        var entries = await db.ActualEntries
            .Include(e => e.Person)
            .Where(e => e.InitiativeId != null && ids.Contains(e.InitiativeId.Value) && !e.IsUnmapped)
            .AsNoTracking()
            .ToListAsync(ct);
        var adjustments = await db.ActualAdjustments.Where(a => ids.Contains(a.InitiativeId)).AsNoTracking().ToListAsync(ct);
        var typeNames = await db.ResourceTypeNamesAsync(ct);
        var cards = await db.PricingRateCardsAsync(ct);

        return PortfolioCalculator.Calculate(initiatives, cards, entries, adjustments, typeNames, defaultThresholdPct);
    }

    /// <summary>Portfolio initiatives matching the dashboard filters, ordered by business unit then name.</summary>
    public static IQueryable<Initiative> FilteredInitiatives(this AppDbContext db, PortfolioFilter filter)
    {
        var query = db.PortfolioInitiatives();
        if (filter.Status is not null)
        {
            query = query.Where(i => i.Status == filter.Status);
        }
        else if (!filter.IncludeClosed)
        {
            query = query.Where(i => i.Status != InitiativeStatus.Complete && i.Status != InitiativeStatus.Cancelled);
        }

        if (filter.BusinessUnitId is not null)
        {
            query = query.Where(i => i.BusinessUnitId == filter.BusinessUnitId);
        }

        return query.OrderBy(i => i.BusinessUnit!.Name).ThenBy(i => i.Name);
    }

    public static IQueryable<Initiative> PortfolioInitiatives(this AppDbContext db, bool includeScenarios = false) =>
        db.Initiatives
            .Where(i => includeScenarios || i.ScenarioOfId == null)
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.ParticipatingBusinessUnits).ThenInclude(p => p.BusinessUnit)
            .Include(i => i.Allocations).ThenInclude(a => a.BusinessUnit)
            .Include(i => i.Allocations).ThenInclude(a => a.Vendor)
            .Include(i => i.Allocations).ThenInclude(a => a.ResourcingClass)
            .Include(i => i.Allocations).ThenInclude(a => a.People).ThenInclude(p => p.Person)
            .Include(i => i.Allocations).ThenInclude(a => a.Seniority)
            .Include(i => i.NonLaborCosts)
            .Include(i => i.Baselines).ThenInclude(b => b.Lines)
            .Include(i => i.Baselines).ThenInclude(b => b.NonLaborLines)
            .Include(i => i.RebaselineRequests)
            .Include(i => i.ChangeRequests).ThenInclude(c => c.ResultingBaseline)
            .AsNoTracking()
            .AsSplitQuery();

    public static Task<Dictionary<int, string>> ResourceTypeNamesAsync(this AppDbContext db, CancellationToken ct) =>
        db.ResourceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

    /// <summary>Cards that price work: published, plus retired cards that kept their effective window (see <see cref="RateResolver.IsPricing"/>).</summary>
    public static Task<List<RateCard>> PricingRateCardsAsync(this AppDbContext db, CancellationToken ct) =>
        db.RateCards.Include(c => c.Entries)
            .Where(c => c.Status == RateCardStatus.Published || c.Status == RateCardStatus.Retired && c.EffectiveEnd != null)
            .AsNoTracking().AsSplitQuery().ToListAsync(ct);
}
