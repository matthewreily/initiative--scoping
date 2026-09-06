using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class RateResolverTests
{
    private static RateCardEntry Entry(int businessUnitId, int typeId = 1, string location = "Onshore") => new()
    {
        ResourceTypeId = typeId, BusinessUnitId = businessUnitId, Seniority = Seniority.Senior, Location = location,
        ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 100m
    };

    private static readonly RateCard[] Cards =
    [
        new() { Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published, Entries = [Entry(1), Entry(2)] },
        new() { Name = "2027", EffectiveStart = new DateOnly(2027, 1, 1), Status = RateCardStatus.Published, Entries = [Entry(1, 2, "Offshore")] },
        new() { Name = "draft", EffectiveStart = new DateOnly(2026, 6, 1), Status = RateCardStatus.Draft, Entries = [Entry(1, 3)] }
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
    public void PricedEntries_are_filtered_to_the_business_unit_of_the_effective_card()
    {
        var bu1In2026 = RateResolver.PricedEntries(Cards, 1, new DateOnly(2026, 7, 1));
        Assert.Single(bu1In2026);
        Assert.Equal(1, bu1In2026[0].ResourceTypeId);

        var bu1In2027 = RateResolver.PricedEntries(Cards, 1, new DateOnly(2027, 3, 1));
        Assert.Equal("Offshore", Assert.Single(bu1In2027).Location);

        Assert.Empty(RateResolver.PricedEntries(Cards, 2, new DateOnly(2027, 3, 1)));
        Assert.Empty(RateResolver.PricedEntries(Cards, 1, new DateOnly(2025, 1, 1)));
    }
}
