using System.Text;
using InitiativeScoping.Application.Exports;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class PortfolioCalculatorTests
{
    private static readonly Dictionary<int, string> TypeNames = new() { [1] = "Software Engineer" };

    private static RateCard Card() => new()
    {
        Id = 1, Name = "c", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
        Entries = [new RateCardEntry { ResourceTypeId = 1, BusinessUnitId = 1, Seniority = Seniority.Senior, Location = "Onshore", ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 100m }]
    };

    private static Initiative Initiative(int id, string bu, InitiativeStatus status, ResourcingClass cls = ResourcingClass.InternalFte, decimal? baselineCost = null, decimal? threshold = null)
    {
        var i = new Initiative
        {
            Id = id, Name = $"I{id}", BusinessUnitId = 1, BusinessUnit = new BusinessUnit { Id = 1, Name = bu }, Status = status,
            TargetStart = new DateOnly(2026, 3, 1), CreatedBy = "u", VarianceThresholdPct = threshold
        };
        i.Phases.Add(new Phase { Id = id * 10, InitiativeId = id, Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 1), PlannedEnd = new DateOnly(2026, 3, 31) });
        i.Allocations.Add(new InitiativeAllocation { Id = id * 100, InitiativeId = id, PhaseId = id * 10, ResourceTypeId = 1, Seniority = Seniority.Senior, Location = "Onshore", ResourcingClass = cls, Quantity = 1, EstimatedHours = 100m });
        if (baselineCost is not null)
        {
            i.Baselines.Add(new ForecastBaseline
            {
                Id = id, InitiativeId = id, Version = 1, IsCurrent = true, SnapshotBy = "u", TotalHours = 100m, TotalCost = baselineCost.Value,
                Lines = [new ForecastBaselineLine { PhaseId = id * 10, ResourceTypeId = 1, Location = "Onshore", Hours = 100m, HourlyRate = baselineCost.Value / 100m, Cost = baselineCost.Value }]
            });
        }
        return i;
    }

    private static ActualEntry Entry(int initiativeId, decimal hours, decimal cost, bool unmapped = false) => new()
    {
        InitiativeId = initiativeId, ExternalProjectId = "P", SourceReference = Guid.NewGuid().ToString(), WorkDate = new DateOnly(2026, 3, 10),
        Hours = hours, CalculatedCost = cost, IsUnmapped = unmapped, PersonId = 1, Person = new Person { Id = 1, DisplayName = "p", ResourceTypeId = 1, Location = "Onshore" }
    };

    [Fact]
    public void Rolls_up_forecast_baseline_and_actuals_per_initiative_and_in_total()
    {
        var a = Initiative(1, "Boarding", InitiativeStatus.Active, baselineCost: 10_000m);
        var b = Initiative(2, "Boarding", InitiativeStatus.Active, ResourcingClass.Vendor, baselineCost: 10_000m, threshold: 10m);
        var c = Initiative(3, "Payments", InitiativeStatus.Draft);
        var entries = new List<ActualEntry> { Entry(1, 10, 1_000m), Entry(2, 50, 12_000m), Entry(2, 5, 999m, unmapped: true), Entry(3, 1, 100m) };
        var adjustments = new List<ActualAdjustment> { new() { InitiativeId = 1, Hours = 0, Cost = 500m, Reason = "r", CreatedBy = "u" } };

        var result = PortfolioCalculator.Calculate([a, b, c], [Card()], entries, adjustments, TypeNames, defaultThresholdPct: 10m);

        Assert.Equal(3, result.Count);
        Assert.Equal(2, result.Baselined);
        Assert.Equal(10_000m, result.Rows[0].ForecastCost);
        Assert.Equal(0m, result.Rows[0].VendorForecastCost);
        Assert.Equal(1_500m, result.Rows[0].ActualCost);
        Assert.Equal(-85.0m, result.Rows[0].CostVariancePct);
        Assert.False(result.Rows[0].ExceedsThreshold);
        Assert.Equal(15.0m, result.Rows[0].BurnPct);

        // Vendor allocation has no rate -> unpriced forecast; unmapped entry excluded; +20% breaches 10% threshold.
        Assert.True(result.Rows[1].HasUnpricedForecast);
        Assert.Equal(12_000m, result.Rows[1].ActualCost);
        Assert.Equal(20.0m, result.Rows[1].CostVariancePct);
        Assert.True(result.Rows[1].ExceedsThreshold);
        Assert.Equal(100m, result.Rows[1].BurnPct);

        // Draft with no baseline: variance % undefined, burn measured against forecast.
        Assert.False(result.Rows[2].HasBaseline);
        Assert.Null(result.Rows[2].CostVariancePct);
        Assert.Equal(1.0m, result.Rows[2].BurnPct);

        Assert.Equal(20_000m, result.ForecastCost);
        Assert.Equal(20_000m, result.BaselineCost);
        Assert.Equal(13_600m, result.ActualCost);
        Assert.Equal(-32.0m, result.CostVariancePct);
        Assert.Equal(1, result.OverThreshold);
        Assert.Equal(1, result.Unpriced);
    }

    [Fact]
    public void Groups_by_business_unit_and_status()
    {
        var a = Initiative(1, "Boarding", InitiativeStatus.Active, baselineCost: 10_000m);
        var b = Initiative(2, "Payments", InitiativeStatus.Active, baselineCost: 10_000m, threshold: 5m);
        var c = Initiative(3, "Payments", InitiativeStatus.OnHold);
        var entries = new List<ActualEntry> { Entry(2, 10, 11_000m) };

        var result = PortfolioCalculator.Calculate([a, b, c], [Card()], entries, [], TypeNames, null);

        var payments = Assert.Single(result.ByBusinessUnit, g => g.Label == "Payments");
        Assert.Equal(2, payments.Count);
        Assert.Equal(20_000m, payments.ForecastCost);
        Assert.Equal(10_000m, payments.BaselineCost);
        Assert.Equal(11_000m, payments.ActualCost);
        Assert.Equal(10.0m, payments.CostVariancePct);
        Assert.Equal(1, payments.OverThreshold);

        Assert.Equal(["Active", "OnHold"], result.ByStatus.Select(g => g.Label));
        Assert.Equal(2, result.ByStatus[0].Count);
        Assert.Null(result.ByStatus[1].CostVariancePct);
    }

    [Fact]
    public void Portfolio_export_has_one_row_per_initiative_and_group_tables()
    {
        var a = Initiative(1, "Boarding", InitiativeStatus.Active, baselineCost: 10_000m);
        var result = PortfolioCalculator.Calculate([a], [Card()], [], [], TypeNames, null);

        var tables = PortfolioExport.Build(result);

        Assert.Equal(["Initiatives", "By business unit", "By status"], tables.Select(t => t.Name));
        var row = Assert.Single(tables[0].Rows);
        Assert.Equal(tables[0].Headers.Count, row.Count);
        Assert.Equal("I1", row[1]);
        Assert.Equal(10_000m, row[tables[0].Headers.ToList().IndexOf("Forecast cost")]);
        Assert.Equal(false, row[tables[0].Headers.ToList().IndexOf("Over threshold")]);
    }
}

public class CsvExportWriterTests
{
    [Fact]
    public void Writes_invariant_values_and_separates_multiple_tables()
    {
        var tables = new List<ExportTable>
        {
            new("Main", ["Name", "Cost", "Date", "Flag", "Note"], [["A, Inc", 1234.5m, new DateOnly(2026, 3, 2), true, null]]),
            new("Groups", ["Group", "Count"], [["G", 2]])
        };

        var text = Encoding.UTF8.GetString(new CsvExportWriter().Write(tables));

        Assert.Equal(
            "# Main\r\nName,Cost,Date,Flag,Note\r\n\"A, Inc\",1234.5,2026-03-02,Yes,\r\n\r\n# Groups\r\nGroup,Count\r\nG,2\r\n",
            text);
    }

    [Fact]
    public void Single_table_has_no_marker()
    {
        var text = Encoding.UTF8.GetString(new CsvExportWriter().Write([new ExportTable("Only", ["A"], [["x"]])]));
        Assert.Equal("A\r\nx\r\n", text);
    }

    [Fact]
    public void Safe_file_name_strips_invalid_characters()
    {
        Assert.Equal("Q3-Launch-v2", ExportFormats.SafeFileName(" Q3 Launch/v2 "));
        Assert.Equal("export", ExportFormats.SafeFileName("///"));
    }
}
