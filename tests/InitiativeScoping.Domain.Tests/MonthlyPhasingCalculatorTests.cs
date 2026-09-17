using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class MonthlyPhasingCalculatorTests
{
    private static readonly DateOnly Jan15 = new(2026, 1, 15);

    private static Initiative Make()
    {
        var i = new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = Jan15 };
        // 15 Jan → 15 Mar 2026: 17 + 28 + 15 = 60 days.
        i.Phases.Add(new Phase { Id = 1, Name = "Build", Sequence = 1, PlannedStart = Jan15, PlannedEnd = new DateOnly(2026, 3, 15) });
        i.Allocations.Add(new InitiativeAllocation
        {
            Id = 1, PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore",
            ResourcingClass = ResourcingClass.InternalFte, Quantity = 1, EstimatedHours = 60
        });
        return i;
    }

    [Fact]
    public void SpreadByDays_is_proportional_and_sums_exactly()
    {
        var shares = MonthlyPhasingCalculator.SpreadByDays(1000m, Jan15, new DateOnly(2026, 3, 15));

        Assert.Equal(3, shares.Count);
        Assert.Equal((new DateOnly(2026, 1, 1), 283.33m), shares[0]);
        Assert.Equal((new DateOnly(2026, 2, 1), 466.67m), shares[1]);
        Assert.Equal((new DateOnly(2026, 3, 1), 250.00m), shares[2]);
        Assert.Equal(1000m, shares.Sum(s => s.Amount));
    }

    [Fact]
    public void SpreadByDays_single_day_window_lands_in_one_month()
    {
        var shares = MonthlyPhasingCalculator.SpreadByDays(99.99m, Jan15, Jan15);
        Assert.Single(shares);
        Assert.Equal(99.99m, shares[0].Amount);
    }

    [Fact]
    public void SpreadByPeriods_dates_each_period_at_its_anniversary()
    {
        var monthly = MonthlyPhasingCalculator.SpreadByPeriods(300m, 3, BillingModel.Monthly, new DateOnly(2026, 11, 20));
        Assert.Equal([new DateOnly(2026, 11, 1), new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 1)], monthly.Select(s => s.Month));
        Assert.All(monthly, s => Assert.Equal(100m, s.Amount));

        var annual = MonthlyPhasingCalculator.SpreadByPeriods(100m, 3, BillingModel.Annual, Jan15);
        Assert.Equal([2026, 2027, 2028], annual.Select(s => s.Month.Year));
        Assert.Equal(100m, annual.Sum(s => s.Amount));
        Assert.Equal(33.34m, annual[^1].Amount);

        var oneTime = MonthlyPhasingCalculator.SpreadByPeriods(500m, 1, BillingModel.OneTime, Jan15);
        Assert.Equal([(new DateOnly(2026, 1, 1), 500m)], oneTime);
    }

    [Fact]
    public void Labor_forecast_and_baseline_follow_phase_days_and_totals_match()
    {
        var i = Make();
        var forecast = new ForecastResult([new ForecastLine(i.Allocations[0], 60m, 100m)]);
        var baseline = new ForecastBaseline
        {
            Version = 1, SnapshotBy = "t", IsCurrent = true, TotalHours = 60, TotalCost = 6000m,
            Lines = [new ForecastBaselineLine { PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", PhaseName = "Build", BusinessUnitName = "BU", ResourceTypeName = "RT", SeniorityName = "S", Hours = 60, HourlyRate = 100m, Cost = 6000m }]
        };

        var phasing = MonthlyPhasingCalculator.Calculate(i, forecast, baseline, [], []);

        Assert.Equal(3, phasing.Months.Count);
        Assert.Equal(1700m, phasing.Months[0].ForecastLaborCost);
        Assert.Equal(17m, phasing.Months[0].ForecastHours);
        Assert.Equal(2800m, phasing.Months[1].ForecastCost);
        Assert.Equal(1500m, phasing.Months[2].BaselineCost);
        Assert.Equal(6000m, phasing.ForecastCost);
        Assert.Equal(6000m, phasing.BaselineCost);
        Assert.Equal(6000m, phasing.Months[^1].CumulativeForecastCost);
        Assert.Equal(4500m, phasing.Months[1].CumulativeBaselineCost);
    }

    [Fact]
    public void Non_labor_actuals_and_adjustments_land_in_their_own_months_and_gaps_are_filled()
    {
        var i = Make();
        var license = new InitiativeNonLaborCost { Id = 5, InitiativeId = i.Id, Description = "Tool", BillingModel = BillingModel.Monthly, Quantity = 1, UnitCost = 50m };
        var forecast = new ForecastResult([], [new NonLaborForecastLine(license, new DateOnly(2026, 6, 10), new DateOnly(2026, 7, 10), 2, 100m)]);
        var entries = new List<ActualEntry>
        {
            new() { InitiativeId = i.Id, ExternalProjectId = "p", SourceReference = "r", WorkDate = new DateOnly(2026, 2, 3), Hours = 8, SourcedCost = 800m },
            new() { InitiativeId = i.Id, ExternalProjectId = "p", SourceReference = "r", WorkDate = new DateOnly(2026, 2, 4), Hours = 8, CalculatedCost = 700m },
            new() { InitiativeId = i.Id, ExternalProjectId = "p", SourceReference = "r", WorkDate = new DateOnly(2026, 2, 5), Hours = 8, IsUnmapped = true, SourcedCost = 999m },
            new() { InitiativeId = 42, ExternalProjectId = "p", SourceReference = "r", WorkDate = new DateOnly(2026, 2, 5), Hours = 8, SourcedCost = 999m }
        };
        var adjustments = new List<ActualAdjustment>
        {
            new() { InitiativeId = i.Id, Cost = 25m, Reason = "r", CreatedBy = "t", CreatedAt = new DateTimeOffset(2026, 4, 30, 23, 0, 0, TimeSpan.FromHours(-5)) }
        };

        var phasing = MonthlyPhasingCalculator.Calculate(i, forecast, null, entries, adjustments);

        Assert.Equal(6, phasing.Months.Count);
        Assert.Equal(new DateOnly(2026, 2, 1), phasing.Months[0].Month);
        Assert.Equal(1500m, phasing.Months[0].ActualCost);
        Assert.Equal(0m, phasing.Months[1].ActualCost);
        Assert.Equal(25m, phasing.Months.Single(m => m.Month.Month == 5).ActualCost);
        Assert.Equal(50m, phasing.Months.Single(m => m.Month.Month == 6).ForecastNonLaborCost);
        Assert.Equal(50m, phasing.Months.Single(m => m.Month.Month == 7).ForecastNonLaborCost);
        Assert.Equal(1525m, phasing.ActualCost);
        Assert.Equal(100m, phasing.ForecastCost);
        Assert.Equal([2026], phasing.ByYear.Select(y => y.Year));
    }

    [Fact]
    public void Empty_inputs_give_empty_profile_and_combine_sums_month_by_month()
    {
        var empty = MonthlyPhasingCalculator.Calculate(new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = Jan15 }, new ForecastResult([]), null, [], []);
        Assert.True(empty.IsEmpty);

        var a = new MonthlyPhasing([new MonthBucket(new DateOnly(2026, 1, 1), 10m, 0m, 1m, 5m, 0m, 10m, 5m, 0m)]);
        var b = new MonthlyPhasing([new MonthBucket(new DateOnly(2026, 3, 1), 20m, 5m, 2m, 0m, 7m, 25m, 0m, 7m)]);

        var combined = MonthlyPhasing.Combine([a, b, empty]);

        Assert.Equal(3, combined.Months.Count);
        Assert.Equal(10m, combined.Months[0].ForecastCost);
        Assert.Equal(0m, combined.Months[1].ForecastCost);
        Assert.Equal(25m, combined.Months[2].ForecastCost);
        Assert.Equal(35m, combined.Months[2].CumulativeForecastCost);
        Assert.Equal(7m, combined.ActualCost);
        Assert.Equal(25m, combined.PeakMonthCost);
    }
}
