using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class NonLaborCostCalculatorTests
{
    private static readonly DateOnly Jan15 = new(2026, 1, 15);

    [Theory]
    [InlineData(BillingModel.OneTime, "2026-01-15", "2026-01-15", 1)]
    [InlineData(BillingModel.OneTime, "2026-01-15", "2027-06-01", 1)]
    [InlineData(BillingModel.Monthly, "2026-01-15", "2026-01-15", 1)]
    [InlineData(BillingModel.Monthly, "2026-01-15", "2026-02-14", 1)]
    [InlineData(BillingModel.Monthly, "2026-01-15", "2026-02-15", 2)]
    [InlineData(BillingModel.Monthly, "2026-01-01", "2026-12-31", 12)]
    [InlineData(BillingModel.Monthly, "2026-01-31", "2026-02-28", 1)]
    [InlineData(BillingModel.Monthly, "2026-01-31", "2026-03-01", 2)]
    [InlineData(BillingModel.Annual, "2026-01-15", "2027-01-14", 1)]
    [InlineData(BillingModel.Annual, "2026-01-15", "2027-01-15", 2)]
    [InlineData(BillingModel.Annual, "2026-01-15", "2026-03-01", 1)]
    public void Whole_periods_are_billed_and_partial_periods_count_as_full(BillingModel model, string start, string end, int expected) =>
        Assert.Equal(expected, NonLaborCostCalculator.BillablePeriods(model, DateOnly.Parse(start), DateOnly.Parse(end)));

    [Theory]
    [InlineData(BillingModel.OneTime)]
    [InlineData(BillingModel.Monthly)]
    [InlineData(BillingModel.Annual)]
    public void End_before_start_bills_nothing(BillingModel model) =>
        Assert.Equal(0, NonLaborCostCalculator.BillablePeriods(model, Jan15, Jan15.AddDays(-1)));

    [Fact]
    public void Cost_is_unit_cost_times_quantity_times_periods_rounded_to_cents()
    {
        Assert.Equal(6_000m, NonLaborCostCalculator.Cost(BillingModel.Monthly, 100m, 5, Jan15, Jan15.AddMonths(11)));
        Assert.Equal(1_000m, NonLaborCostCalculator.Cost(BillingModel.OneTime, 1_000m, 1, Jan15, Jan15.AddYears(3)));
        Assert.Equal(33.34m, NonLaborCostCalculator.Cost(BillingModel.Monthly, 11.1133m, 3, Jan15, Jan15));
        Assert.Equal(0m, NonLaborCostCalculator.Cost(BillingModel.Monthly, 100m, 5, Jan15, Jan15.AddDays(-1)));
    }

    [Fact]
    public void Window_uses_phase_dates_then_initiative_dates_and_explicit_dates_win()
    {
        var initiative = Fixture(targetEnd: new DateOnly(2026, 6, 30));
        var phase = initiative.Phases[0];

        var phaseScoped = new InitiativeNonLaborCost { PhaseId = phase.Id, Description = "x" };
        Assert.Equal((phase.PlannedStart, phase.PlannedEnd), NonLaborCostCalculator.Window(phaseScoped, initiative));

        var wide = new InitiativeNonLaborCost { Description = "x" };
        Assert.Equal((initiative.TargetStart, new DateOnly(2026, 6, 30)), NonLaborCostCalculator.Window(wide, initiative));

        var explicitDates = new InitiativeNonLaborCost
        {
            PhaseId = phase.Id, Description = "x", StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 5, 31)
        };
        Assert.Equal((new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31)), NonLaborCostCalculator.Window(explicitDates, initiative));
    }

    [Fact]
    public void Initiative_end_falls_back_to_last_phase_end_then_target_start()
    {
        var withPhases = Fixture(targetEnd: null);
        Assert.Equal(new DateOnly(2026, 3, 31), NonLaborCostCalculator.InitiativeEnd(withPhases));

        var bare = new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = Jan15 };
        Assert.Equal(Jan15, NonLaborCostCalculator.InitiativeEnd(bare));

        var inverted = new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = Jan15, TargetEnd = Jan15.AddDays(-10) };
        Assert.Equal(Jan15, NonLaborCostCalculator.InitiativeEnd(inverted));
    }

    [Fact]
    public void Missing_phase_yields_no_window_and_blocks_activation()
    {
        var initiative = Fixture(targetEnd: null);
        initiative.NonLaborCosts.Add(new InitiativeNonLaborCost { PhaseId = 999, Description = "orphan", UnitCost = 10m });
        Assert.Null(NonLaborCostCalculator.Window(initiative.NonLaborCosts[0], initiative));

        var forecast = ForecastCalculator.Calculate(initiative, []);
        var line = Assert.Single(forecast.NonLaborLines);
        Assert.False(line.HasWindow);
        Assert.Equal(0, line.Periods);
        Assert.Equal(0m, forecast.NonLaborCost);
    }

    [Fact]
    public void Forecast_adds_non_labor_cost_without_touching_hours()
    {
        var initiative = Fixture(targetEnd: null);
        initiative.NonLaborCosts.Add(new InitiativeNonLaborCost
        {
            PhaseId = initiative.Phases[0].Id, Category = CostCategory.SoftwareLicense, Description = "IDE seats",
            BillingModel = BillingModel.Monthly, Quantity = 4, UnitCost = 25m
        });
        initiative.NonLaborCosts.Add(new InitiativeNonLaborCost
        {
            Category = CostCategory.Hardware, Description = "Laptops", BillingModel = BillingModel.OneTime, Quantity = 2, UnitCost = 1_500m
        });

        var forecast = ForecastCalculator.Calculate(initiative, []);
        Assert.Equal(0m, forecast.TotalHours);
        Assert.Equal(0m, forecast.LaborCost);
        // Jan 15 – Mar 31 = 3 monthly periods × 4 × 25 = 300; laptops 3,000.
        Assert.Equal(3_300m, forecast.NonLaborCost);
        Assert.Equal(3_300m, forecast.TotalCost);
        Assert.Equal(2, forecast.NonLaborLines.Count);
    }

    private static Initiative Fixture(DateOnly? targetEnd) => new()
    {
        Name = "Test", BusinessUnitId = 1, CreatedBy = "t", TargetStart = Jan15, TargetEnd = targetEnd,
        Phases =
        [
            new Phase { Id = 10, Name = "Build", Sequence = 1, PlannedStart = Jan15, PlannedEnd = new DateOnly(2026, 3, 31) }
        ]
    };
}
