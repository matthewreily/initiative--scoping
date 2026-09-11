using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class RateResolverTests
{
    private static RateCardEntry Entry(int typeId = 1, string location = "Onshore", decimal rate = 100m) => new()
    {
        ResourceTypeId = typeId, SeniorityId = 3, Location = location,
        ResourcingClass = ResourcingClass.InternalFte, HourlyRate = rate
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
        var key = new RateKey(1, 3, "Onshore", ResourcingClass.InternalFte);
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
        ResourcingClass = ResourcingClass.Vendor, VendorId = vendorId, HourlyRate = rate
    };

    private static readonly RateCard[] Cards =
    [
        new()
        {
            Id = 1, Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
            Entries =
            [
                new() { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 80m },
                Vendor(null, 100m), Vendor(10, 120m), Vendor(20, 150m)
            ]
        }
    ];

    private static RateKey Key(ResourcingClass cls, int? vendorId) => new(1, 3, "Onshore", cls, vendorId);

    [Fact]
    public void Vendor_specific_rate_beats_generic_vendor_rate()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(120m, RateResolver.Resolve(Cards, Key(ResourcingClass.Vendor, 10), asOf));
        Assert.Equal(150m, RateResolver.Resolve(Cards, Key(ResourcingClass.Vendor, 20), asOf));
    }

    [Fact]
    public void Generic_vendor_rate_is_the_fallback_for_unlisted_vendors()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(100m, RateResolver.Resolve(Cards, Key(ResourcingClass.Vendor, 99), asOf));
        Assert.Equal(100m, RateResolver.Resolve(Cards, Key(ResourcingClass.Vendor, null), asOf));
        Assert.Null(RateResolver.Resolve(Cards, Key(ResourcingClass.Vendor, 10) with { Location = "Mars" }, asOf));
    }

    [Fact]
    public void Internal_rates_ignore_vendor_and_vendor_rows_never_price_internal_work()
    {
        var asOf = new DateOnly(2026, 6, 1);
        Assert.Equal(80m, RateResolver.Resolve(Cards, Key(ResourcingClass.InternalFte, null), asOf));
        Assert.Equal(80m, RateResolver.Resolve(Cards, Key(ResourcingClass.InternalFte, 10), asOf));
        Assert.True(RateResolver.VendorMatches(ResourcingClass.InternalFte, null, 5));
        Assert.False(RateResolver.VendorMatches(ResourcingClass.Vendor, 10, 20));
    }

    [Fact]
    public void PricedEntries_returns_every_entry_of_the_effective_card()
    {
        Assert.Equal(4, RateResolver.PricedEntries(Cards, new DateOnly(2026, 6, 1)).Count);
    }
}
