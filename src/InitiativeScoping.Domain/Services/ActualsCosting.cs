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
            new RateKey(person.ResourceTypeId, person.SeniorityId, person.Location, person.ResourcingClassId, person.VendorId),
            entry.WorkDate);
        return rate is null ? null : Math.Round(entry.Hours * rate.Value, 2);
    }

    /// <summary>
    /// Resolves the roster person for an imported time entry: an external-id match on the roster wins; otherwise,
    /// when the entry's initiative names people on its allocations, a person whose display name equals the
    /// external person id (case-insensitive, unique among those named) is used.
    /// </summary>
    public static Person? ResolvePerson(IReadOnlyList<Person> roster, IReadOnlyList<Person> namedOnInitiative, string? externalPersonId)
    {
        var byId = roster.FirstOrDefault(p => MatchesExternalId(p, externalPersonId));
        if (byId is not null || string.IsNullOrWhiteSpace(externalPersonId))
        {
            return byId;
        }

        var byName = namedOnInitiative
            .Where(p => string.Equals(p.DisplayName.Trim(), externalPersonId.Trim(), StringComparison.OrdinalIgnoreCase))
            .DistinctBy(p => p.Id)
            .ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    public static bool MatchesExternalId(Person person, string? externalId) =>
        !string.IsNullOrWhiteSpace(externalId) &&
        !string.IsNullOrWhiteSpace(person.ExternalIds) &&
        person.ExternalIds.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(externalId.Trim(), StringComparer.OrdinalIgnoreCase);
}
