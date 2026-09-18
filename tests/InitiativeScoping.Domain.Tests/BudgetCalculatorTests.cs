using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class BudgetCalculatorTests
{
    private static readonly Dictionary<int, string> TypeNames = new() { [1] = "Engineer" };

    private static RateCard Card() => new()
    {
        Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
        Entries = [new RateCardEntry { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId, HourlyRate = 100m }]
    };

    /// <summary>100 h × $100 = $10,000 forecast (+ contingency if set).</summary>
    private static Initiative Initiative(decimal? budget, decimal contingencyPct = 0m, string? fiscalYear = null) => new()
    {
        Id = 7, Name = "Test", BusinessUnitId = 1, CreatedBy = "t", TargetStart = new DateOnly(2026, 3, 1),
        ApprovedBudget = budget, BudgetFiscalYear = fiscalYear, ContingencyPct = contingencyPct,
        Phases = [new Phase { Id = 10, Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 2), PlannedEnd = new DateOnly(2026, 3, 31) }],
        Allocations =
        [
            new InitiativeAllocation
            {
                Id = 1, PhaseId = 10, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore",
                ResourcingClassId = ResourcingClass.InternalId, Quantity = 1, EstimatedHours = 100
            }
        ]
    };

    private static BudgetPosition Position(Initiative i, IReadOnlyList<ActualEntry>? entries = null, DateOnly? asOf = null)
    {
        var forecast = ForecastCalculator.Calculate(i, [Card()]);
        var variance = VarianceCalculator.Calculate(i, entries ?? [], [], TypeNames, null, asOf);
        return BudgetCalculator.Calculate(i, forecast, variance);
    }

    [Fact]
    public void No_budget_means_nothing_to_compare_against()
    {
        var position = Position(Initiative(null));

        Assert.False(position.HasBudget);
        Assert.Null(position.Remaining);
        Assert.Null(position.Unspent);
        Assert.Null(position.UtilizationPct);
        Assert.False(position.OverBudget);
        Assert.False(position.NearBudget);
        Assert.Equal(10_000m, position.Expected);
    }

    [Fact]
    public void Without_a_baseline_the_budget_is_compared_against_the_forecast_including_contingency()
    {
        var position = Position(Initiative(12_000m, contingencyPct: 10m, fiscalYear: "FY26"));

        Assert.False(position.UsesEac);
        Assert.Equal("FY26", position.FiscalYear);
        Assert.Equal(11_000m, position.Forecast);
        Assert.Equal(11_000m, position.Expected);
        Assert.Equal(1_000m, position.Remaining);
        Assert.Equal(12_000m, position.Unspent);
        Assert.Equal(91.7m, position.UtilizationPct);
        Assert.False(position.OverBudget);
        Assert.True(position.NearBudget);
    }

    [Fact]
    public void Forecast_above_budget_is_flagged_with_a_negative_remaining()
    {
        var position = Position(Initiative(8_000m));

        Assert.Equal(-2_000m, position.Remaining);
        Assert.True(position.OverBudget);
        Assert.False(position.NearBudget);
        Assert.Equal(125m, position.UtilizationPct);
    }

    [Fact]
    public void Once_actuals_flow_against_a_baseline_the_budget_is_compared_against_the_estimate_at_completion()
    {
        var initiative = Initiative(12_000m, contingencyPct: 10m);
        var forecast = ForecastCalculator.Calculate(initiative, [Card()]);
        var baseline = BaselineSnapshot.Create(initiative, forecast, "t", DateTimeOffset.UtcNow, null);
        baseline.IsCurrent = true;
        initiative.Baselines.Add(baseline);

        // Later contingency edits change the live forecast, not the reserve captured on the baseline.
        initiative.ContingencyPct = 100m;

        // Half-way through the 30-day phase: $7,000 spent, $5,000 of baseline still to come → EAC 12,000 + the baseline's 1,000 reserve.
        var entries = new List<ActualEntry>
        {
            new() { InitiativeId = initiative.Id, ExternalProjectId = "P", SourceReference = "r", WorkDate = new DateOnly(2026, 3, 10), Hours = 70, SourcedCost = 7_000m }
        };
        var position = Position(initiative, entries, asOf: new DateOnly(2026, 3, 16));

        Assert.True(position.UsesEac);
        Assert.Equal(7_000m, position.Actual);
        Assert.Equal(5_000m, position.Unspent);
        Assert.Equal(20_000m, position.Forecast);
        Assert.Equal(13_000m, position.Eac);
        Assert.Equal(13_000m, position.Expected);
        Assert.Equal(-1_000m, position.Remaining);
        Assert.True(position.OverBudget);
        Assert.Equal(108.3m, position.UtilizationPct);
    }

    [Fact]
    public void Recorded_actuals_switch_to_eac_even_when_they_carry_no_cost()
    {
        var initiative = Initiative(20_000m, contingencyPct: 10m);
        var forecast = ForecastCalculator.Calculate(initiative, [Card()]);
        var baseline = BaselineSnapshot.Create(initiative, forecast, "t", DateTimeOffset.UtcNow, null);
        baseline.IsCurrent = true;
        initiative.Baselines.Add(baseline);

        var entries = new List<ActualEntry>
        {
            new() { InitiativeId = initiative.Id, ExternalProjectId = "P", SourceReference = "r", WorkDate = new DateOnly(2026, 3, 10), Hours = 40 }
        };
        var position = Position(initiative, entries, asOf: new DateOnly(2026, 3, 16));

        Assert.True(position.UsesEac);
        Assert.Equal(0m, position.Actual);
        Assert.Equal(6_000m, position.Expected);
        Assert.Equal(14_000m, position.Remaining);
    }

    [Fact]
    public void Baseline_without_actuals_still_uses_the_live_forecast()
    {
        var initiative = Initiative(20_000m);
        var forecast = ForecastCalculator.Calculate(initiative, [Card()]);
        var baseline = BaselineSnapshot.Create(initiative, forecast, "t", DateTimeOffset.UtcNow, null);
        baseline.IsCurrent = true;
        initiative.Baselines.Add(baseline);

        var position = Position(initiative);

        Assert.False(position.UsesEac);
        Assert.Equal(10_000m, position.Expected);
        Assert.Equal(10_000m, position.Remaining);
    }

    [Fact]
    public void Portfolio_rolls_up_budgets_and_counts_over_budget_initiatives()
    {
        var cards = new[] { Card() };
        var rows = new[] { Initiative(15_000m), Initiative(8_000m), Initiative(null) }
            .Select(i =>
            {
                var forecast = ForecastCalculator.Calculate(i, cards);
                return new PortfolioRow(i, forecast, VarianceCalculator.Calculate(i, [], [], TypeNames), MonthlyPhasingCalculator.Calculate(i, forecast, null, [], []));
            })
            .ToList();
        var result = new PortfolioResult(rows);

        Assert.Equal(2, result.Budgeted);
        Assert.Equal(23_000m, result.ApprovedBudget);
        Assert.Equal(3_000m, result.BudgetRemaining);
        Assert.Equal(1, result.OverBudget);
        Assert.Equal(5_000m, rows[0].BudgetRemaining);
        Assert.True(rows[1].OverBudget);
        Assert.Null(rows[2].ApprovedBudget);
        Assert.Null(rows[2].BudgetRemaining);
    }
}
