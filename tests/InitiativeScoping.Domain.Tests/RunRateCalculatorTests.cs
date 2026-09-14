using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class RunRateCalculatorTests
{
    private static Initiative Make(DateOnly start, DateOnly? end) =>
        new() { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = start, TargetEnd = end };

    [Fact]
    public void Spreads_total_cost_over_the_planned_window()
    {
        // 2024-01-01 → 2024-12-31 inclusive is 366 days (leap year).
        var rate = RunRateCalculator.Calculate(Make(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)), 366_000m);

        Assert.Equal(366, rate.Days);
        Assert.Equal(365_250m, rate.Yearly);
        Assert.Equal(30_437.50m, rate.Monthly);
    }

    [Fact]
    public void Falls_back_to_last_phase_end_and_never_divides_by_zero()
    {
        var start = new DateOnly(2025, 3, 1);
        var i = Make(start, null);
        i.Phases.Add(new Phase { Name = "p", PlannedStart = start, PlannedEnd = start.AddDays(29) });

        Assert.Equal(30, RunRateCalculator.Calculate(i, 3_000m).Days);
        Assert.Equal(1, RunRateCalculator.Calculate(Make(start, null), 100m).Days);
        Assert.Equal(0m, RunRateCalculator.Calculate(Make(start, null), 0m).Yearly);
    }
}
