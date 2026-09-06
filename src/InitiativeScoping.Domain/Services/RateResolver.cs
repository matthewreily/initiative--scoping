using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

public readonly record struct RateKey(
    int ResourceTypeId,
    int BusinessUnitId,
    Seniority Seniority,
    string Location,
    ResourcingClass ResourcingClass);

public static class RateResolver
{
    /// <summary>
    /// Picks the published rate card with the latest EffectiveStart on or before <paramref name="asOf"/>
    /// and returns the exact-match entry rate, or null if the allocation is unpriced.
    /// </summary>
    public static decimal? Resolve(IEnumerable<RateCard> rateCards, RateKey key, DateOnly asOf) =>
        EffectiveCard(rateCards, asOf)?.Entries.FirstOrDefault(e =>
                e.ResourceTypeId == key.ResourceTypeId &&
                e.BusinessUnitId == key.BusinessUnitId &&
                e.Seniority == key.Seniority &&
                e.ResourcingClass == key.ResourcingClass &&
                string.Equals(e.Location, key.Location, StringComparison.OrdinalIgnoreCase))
            ?.HourlyRate;

    public static RateCard? EffectiveCard(IEnumerable<RateCard> rateCards, DateOnly asOf) =>
        rateCards
            .Where(c => c.Status == RateCardStatus.Published && c.EffectiveStart <= asOf)
            .OrderByDescending(c => c.EffectiveStart)
            .FirstOrDefault();

    /// <summary>Entries that price <paramref name="businessUnitId"/> on <paramref name="asOf"/>; empty when nothing is published for that BU yet.</summary>
    public static IReadOnlyList<RateCardEntry> PricedEntries(IEnumerable<RateCard> rateCards, int businessUnitId, DateOnly asOf) =>
        EffectiveCard(rateCards, asOf)?.Entries.Where(e => e.BusinessUnitId == businessUnitId).ToList() ?? [];
}
