using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>
/// <paramref name="VendorId"/> is ignored for internal resources. For vendor resources a rate row naming the same vendor wins;
/// a vendor-class row with no vendor is a generic "any vendor" rate that applies when no vendor-specific row exists.
/// </summary>
public readonly record struct RateKey(
    int ResourceTypeId,
    Seniority Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    int? VendorId = null);

public static class RateResolver
{
    /// <summary>
    /// Picks the published rate card with the latest EffectiveStart on or before <paramref name="asOf"/>
    /// and returns the exact-match entry rate, or null if the allocation is unpriced.
    /// </summary>
    public static decimal? Resolve(IEnumerable<RateCard> rateCards, RateKey key, DateOnly asOf) =>
        EffectiveCard(rateCards, asOf)?.Entries
            .Where(e =>
                e.ResourceTypeId == key.ResourceTypeId &&
                e.Seniority == key.Seniority &&
                e.ResourcingClass == key.ResourcingClass &&
                VendorMatches(e.ResourcingClass, e.VendorId, key.VendorId) &&
                string.Equals(e.Location, key.Location, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.VendorId.HasValue)
            .FirstOrDefault()
            ?.HourlyRate;

    /// <summary>True when the row prices the vendor: internal rows always, vendor rows when they name the vendor or name none.</summary>
    public static bool VendorMatches(ResourcingClass cls, int? entryVendorId, int? vendorId) =>
        cls != ResourcingClass.Vendor || entryVendorId is null || entryVendorId == vendorId;

    public static RateCard? EffectiveCard(IEnumerable<RateCard> rateCards, DateOnly asOf) =>
        rateCards
            .Where(c => c.Status == RateCardStatus.Published && c.EffectiveStart <= asOf)
            .OrderByDescending(c => c.EffectiveStart)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

    /// <summary>Entries of the card effective on <paramref name="asOf"/>; empty when nothing is published yet.</summary>
    public static IReadOnlyList<RateCardEntry> PricedEntries(IEnumerable<RateCard> rateCards, DateOnly asOf) =>
        EffectiveCard(rateCards, asOf)?.Entries.ToList() ?? [];
}
