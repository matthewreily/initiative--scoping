using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class FiscalPeriodTests
{
    private static MonthBucket Month(int year, int month, decimal forecast, decimal capex = 0m, decimal baseline = 0m, decimal actual = 0m) =>
        new(new DateOnly(year, month, 1), forecast, 0m, 0m, baseline, actual, 0m, 0m, 0m, capex, 0m);

    [Theory]
    [InlineData(1, 2026, 3, 2026, 1)]
    [InlineData(1, 2026, 12, 2026, 4)]
    [InlineData(7, 2026, 6, 2026, 4)]
    [InlineData(7, 2026, 7, 2027, 1)]
    [InlineData(7, 2027, 3, 2027, 3)]
    [InlineData(4, 2026, 3, 2026, 4)]
    [InlineData(4, 2026, 4, 2027, 1)]
    [InlineData(10, 2026, 1, 2026, 2)]
    public void Fiscal_year_and_quarter_follow_the_start_month(int startMonth, int year, int month, int expectedFy, int expectedQuarter)
    {
        var calendar = new FiscalCalendar(startMonth);
        var date = new DateOnly(year, month, 15);

        Assert.Equal(expectedFy, calendar.FiscalYearOf(date));
        Assert.Equal(expectedQuarter, calendar.QuarterOf(date));
    }

    [Fact]
    public void Labels_and_year_starts_depend_on_start_month()
    {
        Assert.Equal("2026", FiscalCalendar.Calendar.Label(2026));
        Assert.Equal(new DateOnly(2026, 1, 1), FiscalCalendar.Calendar.StartOf(2026));

        var july = new FiscalCalendar(7);
        Assert.Equal("FY2027", july.Label(2027));
        Assert.Equal(new DateOnly(2026, 7, 1), july.StartOf(2027));
    }

    [Fact]
    public void Calendar_year_profile_yields_year_rows_followed_by_quarters()
    {
        var phasing = new MonthlyPhasing([Month(2026, 11, 100m, capex: 40m, baseline: 90m), Month(2026, 12, 100m), Month(2027, 1, 50m, actual: 20m)]);

        var periods = phasing.ByFiscalPeriod(FiscalCalendar.Calendar);

        Assert.Equal(["2026", "2026 Q4", "2027", "2027 Q1"], periods.Select(p => p.Label));
        Assert.Equal([null, 4, null, 1], periods.Select(p => p.Quarter));
        var fy26 = periods[0];
        Assert.Equal(200m, fy26.ForecastCost);
        Assert.Equal(40m, fy26.ForecastCapexCost);
        Assert.Equal(160m, fy26.ForecastOpexCost);
        Assert.Equal(90m, fy26.BaselineCost);
        Assert.Equal(110m, fy26.VarianceToBaseline);
        Assert.Equal(new DateOnly(2026, 1, 1), fy26.Start);
        Assert.Equal(new DateOnly(2026, 12, 31), fy26.End);
        Assert.Equal(new DateOnly(2026, 10, 1), periods[1].Start);
        Assert.Equal(new DateOnly(2026, 12, 31), periods[1].End);
        Assert.Equal(20m, periods[3].ActualCost);
    }

    [Fact]
    public void July_start_groups_months_across_the_calendar_boundary_and_preserves_totals()
    {
        var months = Enumerable.Range(0, 14).Select(k => Month(2026, 1, 10m + k, capex: 1m).Bump(k)).ToList();
        var phasing = new MonthlyPhasing(months);

        var periods = phasing.ByFiscalPeriod(new FiscalCalendar(7));

        Assert.Equal(["FY2026", "FY2026 Q3", "FY2026 Q4", "FY2027", "FY2027 Q1", "FY2027 Q2", "FY2027 Q3"], periods.Select(p => p.Label));
        var years = periods.Where(p => p.Quarter is null).ToList();
        var quarters = periods.Where(p => p.Quarter is not null).ToList();
        Assert.Equal(phasing.ForecastCost, years.Sum(p => p.ForecastCost));
        Assert.Equal(phasing.ForecastCost, quarters.Sum(p => p.ForecastCost));
        Assert.Equal(phasing.ForecastCapexCost, years.Sum(p => p.ForecastCapexCost));
        Assert.Equal(10m + 11m + 12m, quarters.Single(p => p.Label == "FY2026 Q3").ForecastCost);
        Assert.Equal(3m, quarters.Single(p => p.Label == "FY2026 Q3").ForecastCapexCost);
        Assert.Equal(new DateOnly(2026, 7, 1), years[1].Start);
        Assert.Equal(new DateOnly(2027, 6, 30), years[1].End);
        Assert.Equal(8, months.Count(m => new FiscalCalendar(7).FiscalYearOf(m.Month) == 2027));
        Assert.Equal(new DateOnly(2027, 1, 1), quarters[^1].Start);
        Assert.Equal(new DateOnly(2027, 3, 31), quarters[^1].End);
    }

    [Fact]
    public void Empty_profile_has_no_fiscal_periods()
    {
        Assert.Empty(MonthlyPhasing.Empty.ByFiscalPeriod(new FiscalCalendar(4)));
    }

    [Fact]
    public void Capex_lines_are_split_out_of_forecast_and_baseline_phasing()
    {
        var jan15 = new DateOnly(2026, 1, 15);
        var i = new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = jan15 };
        i.Phases.Add(new Phase { Id = 1, Name = "Build", Sequence = 1, PlannedStart = jan15, PlannedEnd = new DateOnly(2026, 3, 15) });
        var capexAlloc = new InitiativeAllocation { Id = 1, PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", Quantity = 1, EstimatedHours = 60, CapexPercent = 100m };
        var opexAlloc = new InitiativeAllocation { Id = 2, PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", Quantity = 1, EstimatedHours = 60 };
        i.Allocations.AddRange([capexAlloc, opexAlloc]);
        var license = new InitiativeNonLaborCost { Id = 5, Description = "Tool", BillingModel = BillingModel.OneTime, Quantity = 1, UnitCost = 500m, CapexPercent = 100m };
        var forecast = new ForecastResult(
            [new ForecastLine(capexAlloc, 60m, 100m), new ForecastLine(opexAlloc, 60m, 50m)],
            [new NonLaborForecastLine(license, jan15, jan15, 1, 500m)]);
        var baseline = new ForecastBaseline
        {
            Version = 1, SnapshotBy = "t", IsCurrent = true,
            Lines = [new ForecastBaselineLine { PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", PhaseName = "Build", BusinessUnitName = "BU", ResourceTypeName = "RT", SeniorityName = "S", ResourcingClassName = "Internal", Hours = 60, HourlyRate = 100m, Cost = 6000m, CapexPercent = 100m }],
            NonLaborLines = [new ForecastBaselineNonLaborLine { Description = "Tool", BillingModel = BillingModel.OneTime, Quantity = 1, UnitCost = 400m, Periods = 1, StartDate = jan15, EndDate = jan15, Cost = 400m }]
        };

        var phasing = MonthlyPhasingCalculator.Calculate(i, forecast, baseline, [], []);

        Assert.Equal(9500m, phasing.ForecastCost);
        Assert.Equal(6500m, phasing.ForecastCapexCost);
        Assert.Equal(3000m, phasing.ForecastOpexCost);
        Assert.Equal(6400m, phasing.BaselineCost);
        Assert.Equal(6000m, phasing.BaselineCapexCost);
        Assert.Equal(1700m + 500m, phasing.Months[0].ForecastCapexCost);
        Assert.Equal(850m, phasing.Months[0].ForecastOpexCost);

        var fy = phasing.ByFiscalPeriod(FiscalCalendar.Calendar).Single(p => p.Quarter is null);
        Assert.Equal(6500m, fy.ForecastCapexCost);
        Assert.Equal(400m, fy.BaselineOpexCost);
    }

    [Fact]
    public void Partial_capex_percent_apportions_cost_and_preserves_totals()
    {
        var jan15 = new DateOnly(2026, 1, 15);
        var i = new Initiative { Name = "n", BusinessUnitId = 1, CreatedBy = "t", TargetStart = jan15 };
        i.Phases.Add(new Phase { Id = 1, Name = "Build", Sequence = 1, PlannedStart = jan15, PlannedEnd = new DateOnly(2026, 1, 31) });
        var alloc = new InitiativeAllocation { Id = 1, PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", Quantity = 1, EstimatedHours = 10, CapexPercent = 60m };
        i.Allocations.Add(alloc);
        var license = new InitiativeNonLaborCost { Id = 5, Description = "Tool", BillingModel = BillingModel.OneTime, Quantity = 1, UnitCost = 333.33m, CapexPercent = 33.33m };
        var forecast = new ForecastResult([new ForecastLine(alloc, 10m, 100m)], [new NonLaborForecastLine(license, jan15, jan15, 1, 333.33m)]);

        var phasing = MonthlyPhasingCalculator.Calculate(i, forecast, null, [], []);

        Assert.Equal(1333.33m, phasing.ForecastCost);
        Assert.Equal(600m + 111.10m, phasing.ForecastCapexCost);
        Assert.Equal(phasing.ForecastCost - phasing.ForecastCapexCost, phasing.ForecastOpexCost);
        Assert.Equal(400m + 222.23m, phasing.ForecastOpexCost);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1000)]
    [InlineData(12.5, 125)]
    [InlineData(150, 1000)]
    [InlineData(-5, 0)]
    public void CapexSplit_clamps_percent_and_sums_to_total(decimal percent, decimal expectedCapex)
    {
        Assert.Equal(expectedCapex, CapexSplit.Capex(1000m, percent));
        Assert.Equal(1000m, CapexSplit.Capex(1000m, percent) + CapexSplit.Opex(1000m, percent));
    }

    [Fact]
    public void CapexSplit_rounds_capex_to_cents_and_labels_split()
    {
        Assert.Equal(0.33m, CapexSplit.Capex(1m, 33.333m));
        Assert.Equal(0.67m, CapexSplit.Opex(1m, 33.333m));
        Assert.Equal("Opex", CapexSplit.Label(0m));
        Assert.Equal("Capex", CapexSplit.Label(100m));
        Assert.Equal("60% Capex", CapexSplit.Label(60m));
    }
}

file static class MonthBucketExtensions
{
    public static MonthBucket Bump(this MonthBucket b, int months) => b with { Month = b.Month.AddMonths(months) };
}
