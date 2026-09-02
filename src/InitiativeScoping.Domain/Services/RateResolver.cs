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
    public static decimal? Resolve(IEnumerable<RateCard> rateCards, RateKey key, DateOnly asOf)
    {
        var card = rateCards
            .Where(c => c.Status == RateCardStatus.Published && c.EffectiveStart <= asOf)
            .OrderByDescending(c => c.EffectiveStart)
            .FirstOrDefault();

        return card?.Entries.FirstOrDefault(e =>
                e.ResourceTypeId == key.ResourceTypeId &&
                e.BusinessUnitId == key.BusinessUnitId &&
                e.Seniority == key.Seniority &&
                e.ResourcingClass == key.ResourcingClass &&
                string.Equals(e.Location, key.Location, StringComparison.OrdinalIgnoreCase))
            ?.HourlyRate;
    }
}
