using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>
/// <paramref name="VendorId"/> is ignored for internal resources. For vendor resources a rate row naming the same vendor wins;
/// a vendor-class row with no vendor is a generic "any vendor" rate that applies when no vendor-specific row exists.
/// </summary>
public readonly record struct RateKey(
    int ResourceTypeId,
    int SeniorityId,
    string Location,
    ResourcingClass ResourcingClass,
    int? VendorId = null);

/// <summary>One stretch of days priced from a single rate card (rate null = unpriced there).</summary>
public sealed record RateSegment(DateOnly Start, DateOnly End, RateCard? Card, decimal? HourlyRate)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
}

public static class RateResolver
{
    /// <summary>
    /// Picks the rate card in effect on <paramref name="asOf"/> and returns the exact-match entry rate,
    /// or null if the allocation is unpriced.
    /// </summary>
    public static decimal? Resolve(IEnumerable<RateCard> rateCards, RateKey key, DateOnly asOf) =>
        Lookup(EffectiveCard(rateCards, asOf), key);

    /// <summary>
    /// Day-weighted average of the rates in effect between <paramref name="start"/> and <paramref name="end"/> inclusive,
    /// so a card change part-way through a phase prices each day at its own rate. Null if any day is unpriced.
    /// </summary>
    public static decimal? ResolveBlended(IEnumerable<RateCard> rateCards, RateKey key, DateOnly start, DateOnly end) =>
        Blend(Segments(rateCards, key, start, end));

    public static decimal? Blend(IReadOnlyList<RateSegment> segments)
    {
        if (segments.Count == 0 || segments.Any(s => s.HourlyRate is null))
        {
            return null;
        }

        var days = segments.Sum(s => s.Days);
        return Math.Round(segments.Sum(s => s.HourlyRate!.Value * s.Days) / days, 4);
    }

    /// <summary>Splits [start, end] at every rate card boundary and prices each stretch; end before start yields a single-day segment at start.</summary>
    public static IReadOnlyList<RateSegment> Segments(IEnumerable<RateCard> rateCards, RateKey key, DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            end = start;
        }

        var cards = rateCards.Where(IsPricing).ToList();
        var boundaries = cards
            .SelectMany(c => new[] { c.EffectiveStart, c.EffectiveEnd?.AddDays(1) })
            .Where(d => d is not null && d > start && d <= end)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var result = new List<RateSegment>();
        var cursor = start;
        foreach (var boundary in boundaries.Append(end.AddDays(1)))
        {
            var card = EffectiveCard(cards, cursor);
            var segEnd = boundary.AddDays(-1);
            if (result.Count > 0 && ReferenceEquals(result[^1].Card, card))
            {
                result[^1] = result[^1] with { End = segEnd };
            }
            else
            {
                result.Add(new RateSegment(cursor, segEnd, card, Lookup(card, key)));
            }

            cursor = boundary;
        }

        return result;
    }

    /// <summary>True when the row prices the vendor: internal rows always, vendor rows when they name the vendor or name none.</summary>
    public static bool VendorMatches(ResourcingClass cls, int? entryVendorId, int? vendorId) =>
        cls != ResourcingClass.Vendor || entryVendorId is null || entryVendorId == vendorId;

    /// <summary>
    /// Published cards price from EffectiveStart until EffectiveEnd (or until a later card starts).
    /// Retired cards keep pricing the window they were retired with, so retiring never rewrites history.
    /// </summary>
    public static bool IsPricing(RateCard card) =>
        card.Status == RateCardStatus.Published || card.Status == RateCardStatus.Retired && card.EffectiveEnd is not null;

    public static bool Covers(RateCard card, DateOnly asOf) =>
        IsPricing(card) && card.EffectiveStart <= asOf && (card.EffectiveEnd is null || asOf <= card.EffectiveEnd);

    public static RateCard? EffectiveCard(IEnumerable<RateCard> rateCards, DateOnly asOf) =>
        rateCards
            .Where(c => Covers(c, asOf))
            .OrderByDescending(c => c.EffectiveStart)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

    /// <summary>Entries of the card effective on <paramref name="asOf"/>; empty when nothing covers that date.</summary>
    public static IReadOnlyList<RateCardEntry> PricedEntries(IEnumerable<RateCard> rateCards, DateOnly asOf) =>
        EffectiveCard(rateCards, asOf)?.Entries.ToList() ?? [];

    private static decimal? Lookup(RateCard? card, RateKey key) =>
        card?.Entries
            .Where(e =>
                e.ResourceTypeId == key.ResourceTypeId &&
                e.SeniorityId == key.SeniorityId &&
                e.ResourcingClass == key.ResourcingClass &&
                VendorMatches(e.ResourcingClass, e.VendorId, key.VendorId) &&
                string.Equals(e.Location, key.Location, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.VendorId.HasValue)
            .FirstOrDefault()
            ?.HourlyRate;
}
