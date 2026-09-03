using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public static class ActualsCosting
{
    /// <summary>
    /// Prices an actual entry from the person roster: the person's resource type/seniority/location/class and
    /// business unit, against the published card in effect on the work date. Null when unmapped or unpriced.
    /// </summary>
    public static decimal? Calculate(ActualEntry entry, Person? person, IEnumerable<RateCard> rateCards)
    {
        if (person is null)
        {
            return null;
        }

        var rate = RateResolver.Resolve(rateCards,
            new RateKey(person.ResourceTypeId, person.BusinessUnitId, person.Seniority, person.Location, person.ResourcingClass),
            entry.WorkDate);
        return rate is null ? null : Math.Round(entry.Hours * rate.Value, 2);
    }

    public static bool MatchesExternalId(Person person, string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) &&
        !string.IsNullOrWhiteSpace(person.ExternalIds) &&
        person.ExternalIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(externalId.Trim(), StringComparer.OrdinalIgnoreCase);
}
