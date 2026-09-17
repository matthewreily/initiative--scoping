using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class ContingencyTests
{
    private static RateCard Card() => new()
    {
        Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
        Entries = [new RateCardEntry { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 100m }]
    };

    private static Initiative Initiative(decimal contingencyPct, EstimateConfidence? confidence = null) => new()
    {
        Name = "Test", BusinessUnitId = 1, CreatedBy = "t", TargetStart = new DateOnly(2026, 3, 1),
        ContingencyPct = contingencyPct, EstimateConfidence = confidence,
        Phases = [new Phase { Id = 10, Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 1), PlannedEnd = new DateOnly(2026, 3, 31) }],
        Allocations =
        [
            new InitiativeAllocation
            {
                PhaseId = 10, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore",
                ResourcingClass = ResourcingClass.InternalFte, Quantity = 1, EstimatedHours = 100
            }
        ],
        NonLaborCosts =
        [
            new InitiativeNonLaborCost { Description = "License", BillingModel = BillingModel.OneTime, Quantity = 1, UnitCost = 2_000m, StartDate = new DateOnly(2026, 3, 1) }
        ]
    };

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(1000, 0, 0)]
    [InlineData(1000, -5, 0)]
    [InlineData(-1000, 10, 0)]
    [InlineData(12_000, 10, 1_200)]
    [InlineData(333.33, 7.5, 25)]
    public void Reserve_is_pct_of_cost_rounded_to_cents_and_never_negative(decimal cost, decimal pct, decimal expected)
        => Assert.Equal(expected, ContingencyCalculator.Reserve(cost, pct));

    [Fact]
    public void Forecast_applies_contingency_to_labor_plus_non_labor_without_changing_base_totals()
    {
        var forecast = ForecastCalculator.Calculate(Initiative(15m), [Card()]);

        Assert.Equal(10_000m, forecast.LaborCost);
        Assert.Equal(2_000m, forecast.NonLaborCost);
        Assert.Equal(12_000m, forecast.TotalCost);
        Assert.Equal(15m, forecast.ContingencyPct);
        Assert.Equal(1_800m, forecast.ContingencyCost);
        Assert.Equal(13_800m, forecast.TotalCostWithContingency);
        Assert.Equal(100m, forecast.TotalHours);
    }

    [Fact]
    public void Forecast_without_contingency_has_zero_reserve()
    {
        var forecast = ForecastCalculator.Calculate(Initiative(0m), [Card()]);

        Assert.Equal(0m, forecast.ContingencyCost);
        Assert.Equal(forecast.TotalCost, forecast.TotalCostWithContingency);
    }

    [Fact]
    public void Baseline_snapshots_contingency_and_confidence_at_capture_time()
    {
        var initiative = Initiative(10m, EstimateConfidence.Medium);
        var forecast = ForecastCalculator.Calculate(initiative, [Card()]);
        var baseline = BaselineSnapshot.Create(initiative, forecast, "t", DateTimeOffset.UtcNow, null);

        Assert.Equal(12_000m, baseline.TotalCost);
        Assert.Equal(10m, baseline.ContingencyPct);
        Assert.Equal(1_200m, baseline.ContingencyCost);
        Assert.Equal(13_200m, baseline.TotalCostWithContingency);
        Assert.Equal(EstimateConfidence.Medium, baseline.EstimateConfidence);

        initiative.ContingencyPct = 50m;
        initiative.EstimateConfidence = EstimateConfidence.Low;
        Assert.Equal(10m, baseline.ContingencyPct);
        Assert.Equal(EstimateConfidence.Medium, baseline.EstimateConfidence);
    }

    [Fact]
    public void Portfolio_rolls_up_contingency_and_counts_confidence()
    {
        var cards = new[] { Card() };
        var low = Initiative(20m, EstimateConfidence.Low);
        var unrated = Initiative(0m);
        var rows = new[] { low, unrated }
            .Select(i => new PortfolioRow(i, ForecastCalculator.Calculate(i, cards), VarianceCalculator.Calculate(i, [], [], new Dictionary<int, string>()), MonthlyPhasingCalculator.Calculate(i, ForecastCalculator.Calculate(i, cards), null, [], [])))
            .ToList();
        var result = new PortfolioResult(rows);

        Assert.Equal(24_000m, result.ForecastCost);
        Assert.Equal(2_400m, result.ContingencyCost);
        Assert.Equal(26_400m, result.ForecastCostWithContingency);
        Assert.Equal(1, result.LowConfidence);
        Assert.Equal(1, result.UnratedConfidence);
        Assert.Equal(EstimateConfidence.Low, rows[0].Confidence);
    }
}
