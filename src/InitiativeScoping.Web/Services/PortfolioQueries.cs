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

        var initiatives = await query.OrderBy(i => i.BusinessUnit!.Name).ThenBy(i => i.Name).ToListAsync(ct);
        var ids = initiatives.Select(i => i.Id).ToList();
        var entries = await db.ActualEntries
            .Include(e => e.Person)
            .Where(e => e.InitiativeId != null && ids.Contains(e.InitiativeId.Value) && !e.IsUnmapped)
            .AsNoTracking()
            .ToListAsync(ct);
        var adjustments = await db.ActualAdjustments.Where(a => ids.Contains(a.InitiativeId)).AsNoTracking().ToListAsync(ct);
        var typeNames = await db.ResourceTypeNamesAsync(ct);
        var cards = await db.PublishedRateCardsAsync(ct);

        return PortfolioCalculator.Calculate(initiatives, cards, entries, adjustments, typeNames, defaultThresholdPct);
    }

    public static IQueryable<Initiative> PortfolioInitiatives(this AppDbContext db) =>
        db.Initiatives
            .Include(i => i.BusinessUnit)
            .Include(i => i.Members)
            .Include(i => i.Phases)
            .Include(i => i.Allocations)
            .Include(i => i.NonLaborCosts)
            .Include(i => i.Baselines).ThenInclude(b => b.Lines)
            .Include(i => i.Baselines).ThenInclude(b => b.NonLaborLines)
            .Include(i => i.RebaselineRequests)
            .AsNoTracking()
            .AsSplitQuery();

    public static Task<Dictionary<int, string>> ResourceTypeNamesAsync(this AppDbContext db, CancellationToken ct) =>
        db.ResourceTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);

    public static Task<List<RateCard>> PublishedRateCardsAsync(this AppDbContext db, CancellationToken ct) =>
        db.RateCards.Include(c => c.Entries).Where(c => c.Status == RateCardStatus.Published).AsNoTracking().AsSplitQuery().ToListAsync(ct);
}
