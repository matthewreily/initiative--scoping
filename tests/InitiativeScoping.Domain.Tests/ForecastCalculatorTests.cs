using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class ForecastCalculatorTests
{
    private static RateCard Card(string name, DateOnly effective, decimal rate, RateCardStatus status = RateCardStatus.Published) => new()
    {
        Name = name,
        EffectiveStart = effective,
        Status = status,
        Entries =
        [
            new RateCardEntry
            {
                ResourceTypeId = 1, BusinessUnitId = 1, Seniority = Seniority.Senior, Location = "Onshore",
                ResourcingClass = ResourcingClass.InternalFte, HourlyRate = rate
            }
        ]
    };

    private static Initiative InitiativeWithOneAllocation(DateOnly phaseStart, decimal hours = 100, int quantity = 2) => new()
    {
        Name = "Test", BusinessUnitId = 1, CreatedBy = "t", TargetStart = phaseStart,
        Phases = [new Phase { Id = 10, Name = "Build", Sequence = 1, PlannedStart = phaseStart, PlannedEnd = phaseStart.AddMonths(1) }],
        Allocations =
        [
            new InitiativeAllocation
            {
                PhaseId = 10, ResourceTypeId = 1, Seniority = Seniority.Senior, Location = "Onshore",
                ResourcingClass = ResourcingClass.InternalFte, Quantity = quantity, EstimatedHours = hours
            }
        ]
    };

    [Fact]
    public void Cost_is_quantity_times_hours_times_rate()
    {
        var result = ForecastCalculator.Calculate(
            InitiativeWithOneAllocation(new DateOnly(2026, 3, 1)),
            [Card("2026", new DateOnly(2026, 1, 1), 120m)]);

        Assert.True(result.IsComplete);
        Assert.Equal(200m, result.TotalHours);
        Assert.Equal(24_000m, result.TotalCost);
    }

    [Fact]
    public void Uses_latest_published_card_effective_on_or_before_phase_start()
    {
        var cards = new[]
        {
            Card("2026", new DateOnly(2026, 1, 1), 100m),
            Card("2027", new DateOnly(2027, 1, 1), 150m),
            Card("2027-draft", new DateOnly(2027, 1, 1), 999m, RateCardStatus.Draft)
        };

        var in2026 = ForecastCalculator.Calculate(InitiativeWithOneAllocation(new DateOnly(2026, 6, 1), 10, 1), cards);
        var in2027 = ForecastCalculator.Calculate(InitiativeWithOneAllocation(new DateOnly(2027, 2, 1), 10, 1), cards);

        Assert.Equal(1_000m, in2026.TotalCost);
        Assert.Equal(1_500m, in2027.TotalCost);
    }

    [Fact]
    public void Missing_exact_match_marks_line_unpriced_and_forecast_incomplete()
    {
        var initiative = InitiativeWithOneAllocation(new DateOnly(2026, 3, 1));
        initiative.Allocations[0].Location = "Offshore";

        var result = ForecastCalculator.Calculate(initiative, [Card("2026", new DateOnly(2026, 1, 1), 120m)]);

        Assert.False(result.IsComplete);
        Assert.True(result.Lines.Single().IsUnpriced);
        Assert.Equal(0m, result.TotalCost);
    }

    [Fact]
    public void No_card_effective_before_phase_start_is_unpriced()
    {
        var result = ForecastCalculator.Calculate(
            InitiativeWithOneAllocation(new DateOnly(2025, 6, 1)),
            [Card("2026", new DateOnly(2026, 1, 1), 120m)]);

        Assert.False(result.IsComplete);
    }
}
