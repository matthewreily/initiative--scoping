using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class RateResolverTests
{
    private static RateCardEntry Entry(int typeId = 1, string location = "Onshore", decimal rate = 100m) => new()
    {
        ResourceTypeId = typeId, SeniorityId = 3, Location = location,
        ResourcingClassId = ResourcingClass.InternalId, HourlyRate = rate
    };

    private static readonly RateCard[] Cards =
    [
        new() { Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published, Entries = [Entry(1), Entry(2)] },
        new() { Name = "2027", EffectiveStart = new DateOnly(2027, 1, 1), Status = RateCardStatus.Published, Entries = [Entry(2, "Offshore")] },
        new() { Name = "draft", EffectiveStart = new DateOnly(2026, 6, 1), Status = RateCardStatus.Draft, Entries = [Entry(3)] }
    ];

    [Fact]
    public void EffectiveCard_is_latest_published_on_or_before_date()
    {
        Assert.Equal("2026", RateResolver.EffectiveCard(Cards, new DateOnly(2026, 12, 31))?.Name);
        Assert.Equal("2027", RateResolver.EffectiveCard(Cards, new DateOnly(2027, 1, 1))?.Name);
        Assert.Null(RateResolver.EffectiveCard(Cards, new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void EffectiveCard_breaks_equal_effective_dates_by_highest_id()
    {
        RateCard[] cards =
        [
            new() { Id = 7, Name = "later-published", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published },
            new() { Id = 3, Name = "earlier-published", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published }
        ];

        Assert.Equal("later-published", RateResolver.EffectiveCard(cards, new DateOnly(2026, 6, 1))?.Name);
        Assert.Equal("later-published", RateResolver.EffectiveCard(cards.Reverse(), new DateOnly(2026, 6, 1))?.Name);
    }

    [Fact]
    public void PricedEntries_are_those_of_the_effective_card()
    {
        Assert.Equal(2, RateResolver.PricedEntries(Cards, new DateOnly(2026, 7, 1)).Count);

        var in2027 = RateResolver.PricedEntries(Cards, new DateOnly(2027, 3, 1));
        Assert.Equal("Offshore", Assert.Single(in2027).Location);

        Assert.Empty(RateResolver.PricedEntries(Cards, new DateOnly(2025, 1, 1)));
    }

    [Fact]
    public void Rates_do_not_depend_on_a_business_unit()
    {
        var key = new RateKey(1, 3, "Onshore", ResourcingClass.InternalId);
        Assert.Equal(100m, RateResolver.Resolve(Cards, key, new DateOnly(2026, 6, 1)));
        Assert.Null(RateResolver.Resolve(Cards, key with { ResourceTypeId = 3 }, new DateOnly(2026, 6, 1)));
        Assert.Null(RateResolver.Resolve(Cards, key with { Location = "Offshore" }, new DateOnly(2026, 6, 1)));
    }
}

public class VendorRateResolverTests
{
    private static RateCardEntry Vendor(int? vendorId, decimal rate) => new()
    {
        ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore",
        ResourcingClassId = ResourcingClass.VendorId, VendorId = vendorId, HourlyRate = rate
    };

    private static readonly RateCard[] Cards =
    [
        new()
        {
            Id = 1, Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
            Entries =
            [
                new() { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId, HourlyRate = 80m },
                Vendor(null, 100m), Vendor(10, 120m), Vendor(20, 150m)
            ]
        }
    ];

    private static RateKey Key(int cls, int? vendorId) => new(1, 3, "Onshore", cls, vendorId);

    [Fact]
    public void Vendor_specific_rate_beats_generic_vendor_rate()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(120m, RateResolver.Resolve(Cards, Key(ResourcingClass.VendorId, 10), asOf));
        Assert.Equal(150m, RateResolver.Resolve(Cards, Key(ResourcingClass.VendorId, 20), asOf));
    }

    [Fact]
    public void Generic_vendor_rate_is_the_fallback_for_unlisted_vendors()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(100m, RateResolver.Resolve(Cards, Key(ResourcingClass.VendorId, 99), asOf));
        Assert.Equal(100m, RateResolver.Resolve(Cards, Key(ResourcingClass.VendorId, null), asOf));
        Assert.Null(RateResolver.Resolve(Cards, Key(ResourcingClass.VendorId, 10) with { Location = "Mars" }, asOf));
    }

    [Fact]
    public void Internal_rates_ignore_vendor_and_vendor_rows_never_price_internal_work()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(80m, RateResolver.Resolve(Cards, Key(ResourcingClass.InternalId, null), asOf));
        Assert.Equal(80m, RateResolver.Resolve(Cards, Key(ResourcingClass.InternalId, 10), asOf));
        Assert.True(RateResolver.VendorMatches(null, 5));
        Assert.False(RateResolver.VendorMatches(10, 20));
    }

    [Fact]
    public void PricedEntries_returns_every_entry_of_the_effective_card()
    {
        Assert.Equal(4, RateResolver.PricedEntries(Cards, new DateOnly(2026, 6, 1)).Count);
    }
}

public class EffectiveWindowResolverTests
{
    private static RateCard Card(int id, string name, DateOnly start, DateOnly? end, decimal rate, RateCardStatus status = RateCardStatus.Published) => new()
    {
        Id = id, Name = name, EffectiveStart = start, EffectiveEnd = end, Status = status,
        Entries = [new() { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId, HourlyRate = rate }]
    };

    private static readonly RateKey Key = new(1, 3, "Onshore", ResourcingClass.InternalId);

    [Fact]
    public void Retired_card_with_an_end_keeps_pricing_its_window()
    {
        RateCard[] cards =
        [
            Card(1, "H1", new(2026, 1, 1), new(2026, 6, 30), 100m, RateCardStatus.Retired),
            Card(2, "H2", new(2026, 7, 1), null, 120m)
        ];

        Assert.Equal(100m, RateResolver.Resolve(cards, Key, new(2026, 3, 1)));
        Assert.Equal(120m, RateResolver.Resolve(cards, Key, new(2026, 7, 1)));
    }

    [Fact]
    public void Retired_card_without_an_end_never_prices()
    {
        RateCard[] cards = [Card(1, "old", new(2026, 1, 1), null, 100m, RateCardStatus.Retired)];
        Assert.Null(RateResolver.Resolve(cards, Key, new(2026, 3, 1)));
        Assert.False(RateResolver.IsPricing(cards[0]));
    }

    [Fact]
    public void Published_card_stops_pricing_after_its_end()
    {
        RateCard[] cards =
        [
            Card(1, "base", new(2026, 1, 1), null, 100m),
            Card(2, "promo", new(2026, 3, 1), new(2026, 3, 31), 50m)
        ];

        Assert.Equal(50m, RateResolver.Resolve(cards, Key, new(2026, 3, 15)));
        Assert.Equal(100m, RateResolver.Resolve(cards, Key, new(2026, 4, 1)));
        Assert.Equal(100m, RateResolver.Resolve(cards, Key, new(2026, 2, 1)));
    }

    [Fact]
    public void Blended_rate_is_day_weighted_across_card_boundaries()
    {
        RateCard[] cards =
        [
            Card(1, "2026", new(2026, 1, 1), null, 100m),
            Card(2, "2027", new(2027, 1, 1), null, 200m)
        ];

        // 10 days at 100 (Dec 22-31) + 10 days at 200 (Jan 1-10) = 150.
        var segments = RateResolver.Segments(cards, Key, new(2026, 12, 22), new(2027, 1, 10));
        Assert.Equal(2, segments.Count);
        Assert.Equal((new DateOnly(2026, 12, 22), new DateOnly(2026, 12, 31), 10, 100m), (segments[0].Start, segments[0].End, segments[0].Days, segments[0].HourlyRate));
        Assert.Equal((new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 10), 10, 200m), (segments[1].Start, segments[1].End, segments[1].Days, segments[1].HourlyRate));
        Assert.Equal(150m, RateResolver.ResolveBlended(cards, Key, new(2026, 12, 22), new(2027, 1, 10)));

        Assert.Equal(100m, RateResolver.ResolveBlended(cards, Key, new(2026, 3, 1), new(2026, 3, 31)));
        Assert.Single(RateResolver.Segments(cards, Key, new(2026, 3, 1), new(2026, 3, 31)));
    }

    [Fact]
    public void Blended_rate_is_null_when_any_day_is_unpriced()
    {
        RateCard[] cards = [Card(1, "2027", new(2027, 1, 1), null, 200m)];
        Assert.Null(RateResolver.ResolveBlended(cards, Key, new(2026, 12, 25), new(2027, 1, 5)));
        Assert.Equal(200m, RateResolver.ResolveBlended(cards, Key, new(2027, 1, 1), new(2027, 1, 5)));
    }

    [Fact]
    public void End_before_start_prices_a_single_day()
    {
        RateCard[] cards = [Card(1, "2026", new(2026, 1, 1), null, 100m)];
        var seg = Assert.Single(RateResolver.Segments(cards, Key, new(2026, 5, 5), new(2026, 5, 1)));
        Assert.Equal(1, seg.Days);
    }
}
