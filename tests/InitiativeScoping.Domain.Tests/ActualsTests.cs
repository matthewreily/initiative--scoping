using InitiativeScoping.Application.Actuals;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class ActualsCsvTests
{
    private const string Header = "ExternalProjectId,ExternalPersonId,WorkDate,Hours,Cost,Reference\n";

    [Fact]
    public void Parses_rows_with_optional_cost_and_default_reference()
    {
        var result = ActualsCsv.Parse(new StringReader(
            "ExternalProjectId,ExternalPersonId,WorkDate,Hours\n" +
            " PV-1 , jane@x.com , 2026-03-02 , 7.5 \n" +
            "PV-1,,2026-03-03,8\n"), "file.csv");

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("file.csv#2", result.Rows[0].SourceReference);
        Assert.Equal("PV-1", result.Rows[0].ExternalProjectId);
        Assert.Equal("jane@x.com", result.Rows[0].ExternalPersonId);
        Assert.Equal(new DateOnly(2026, 3, 2), result.Rows[0].WorkDate);
        Assert.Equal(7.5m, result.Rows[0].Hours);
        Assert.Null(result.Rows[0].Cost);
        Assert.Null(result.Rows[1].ExternalPersonId);
    }

    [Fact]
    public void Parses_sourced_cost_and_explicit_reference()
    {
        var result = ActualsCsv.Parse(new StringReader(Header + "PV-1,p1,2026-03-02,8,$960.00,TS-1\nPV-1,p1,2026-03-03,8,,TS-2\n"), "f");

        Assert.True(result.IsValid);
        Assert.Equal(960m, result.Rows[0].Cost);
        Assert.Equal("TS-1", result.Rows[0].SourceReference);
        Assert.Null(result.Rows[1].Cost);
    }

    [Fact]
    public void Reports_missing_columns_bad_values_and_duplicate_references()
    {
        Assert.Contains(ActualsCsv.Parse(new StringReader("ExternalProjectId,Hours\nx,1\n"), "f").Errors, e => e.Message.Contains("Missing column"));
        Assert.Contains(ActualsCsv.Parse(new StringReader(string.Empty), "f").Errors, e => e.Message.Contains("header"));

        var result = ActualsCsv.Parse(new StringReader(Header +
            ",p1,2026-03-02,8,,a\n" +
            "PV-1,p1,03/02/2026,8,,b\n" +
            "PV-1,p1,2026-03-02,0,,c\n" +
            "PV-1,p1,2026-03-02,8,abc,d\n" +
            "PV-1,p1,2026-03-02,8,,DUP\n" +
            "PV-1,p1,2026-03-03,8,,dup\n"), "f");

        Assert.False(result.IsValid);
        Assert.Equal(5, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Line == 2 && e.Message.Contains("ExternalProjectId"));
        Assert.Contains(result.Errors, e => e.Line == 3 && e.Message.Contains("WorkDate"));
        Assert.Contains(result.Errors, e => e.Line == 4 && e.Message.Contains("Hours"));
        Assert.Contains(result.Errors, e => e.Line == 5 && e.Message.Contains("Cost"));
        Assert.Contains(result.Errors, e => e.Message.Contains("Duplicate Reference"));
    }
}

public class ActualsCostingTests
{
    private static Person Jane(string? ids = "PV-42;jane@x.com") => new()
    {
        Id = 1, DisplayName = "Jane", ExternalIds = ids, ResourceTypeId = 1, BusinessUnitId = 1,
        Seniority = Seniority.Senior, Location = "Onshore", ResourcingClass = ResourcingClass.InternalFte
    };

    private static RateCard Card(DateOnly effective, decimal rate) => new()
    {
        Id = (int)rate, Name = "c", EffectiveStart = effective, Status = RateCardStatus.Published,
        Entries = [new RateCardEntry { ResourceTypeId = 1, BusinessUnitId = 1, Seniority = Seniority.Senior, Location = "Onshore", ResourcingClass = ResourcingClass.InternalFte, HourlyRate = rate }]
    };

    private static ActualEntry Entry(DateOnly date, decimal hours = 8m) => new() { ExternalProjectId = "P", SourceReference = "r", WorkDate = date, Hours = hours };

    [Fact]
    public void Matches_external_ids_case_insensitively_across_separators()
    {
        Assert.True(ActualsCosting.MatchesExternalId(Jane(), "pv-42"));
        Assert.True(ActualsCosting.MatchesExternalId(Jane(), " JANE@X.COM "));
        Assert.False(ActualsCosting.MatchesExternalId(Jane(), "PV-4"));
        Assert.False(ActualsCosting.MatchesExternalId(Jane(), null));
        Assert.False(ActualsCosting.MatchesExternalId(Jane(ids: null), "PV-42"));
    }

    [Fact]
    public void Prices_from_the_card_in_effect_on_the_work_date()
    {
        var cards = new[] { Card(new DateOnly(2026, 1, 1), 100m), Card(new DateOnly(2026, 7, 1), 130m) };

        Assert.Equal(800m, ActualsCosting.Calculate(Entry(new DateOnly(2026, 3, 1)), Jane(), cards));
        Assert.Equal(1040m, ActualsCosting.Calculate(Entry(new DateOnly(2026, 7, 1)), Jane(), cards));
        Assert.Null(ActualsCosting.Calculate(Entry(new DateOnly(2025, 12, 31)), Jane(), cards));
    }

    [Fact]
    public void Unpriced_when_no_person_or_no_exact_rate()
    {
        var cards = new[] { Card(new DateOnly(2026, 1, 1), 100m) };
        var offshore = Jane();
        offshore.Location = "Offshore";

        Assert.Null(ActualsCosting.Calculate(Entry(new DateOnly(2026, 3, 1)), null, cards));
        Assert.Null(ActualsCosting.Calculate(Entry(new DateOnly(2026, 3, 1)), offshore, cards));
    }
}

public class VarianceCalculatorTests
{
    private static readonly Dictionary<int, string> Types = new() { [1] = "Engineer", [2] = "QA" };

    private static Initiative Initiative(bool withBaseline = true, decimal? threshold = null)
    {
        var i = new Initiative { Id = 5, Name = "I", BusinessUnitId = 1, TargetStart = new DateOnly(2026, 3, 1), CreatedBy = "u", VarianceThresholdPct = threshold };
        i.Phases.Add(new Phase { Id = 1, InitiativeId = 5, Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 1), PlannedEnd = new DateOnly(2026, 3, 31) });
        i.Phases.Add(new Phase { Id = 2, InitiativeId = 5, Name = "Launch", Sequence = 2, PlannedStart = new DateOnly(2026, 4, 1), PlannedEnd = new DateOnly(2026, 4, 30) });
        if (withBaseline)
        {
            i.Baselines.Add(new ForecastBaseline
            {
                Id = 1, InitiativeId = 5, Version = 1, IsCurrent = true, SnapshotBy = "u", TotalHours = 300m, TotalCost = 30_000m,
                Lines =
                [
                    new ForecastBaselineLine { PhaseId = 1, ResourceTypeId = 1, Location = "Onshore", Hours = 200m, HourlyRate = 100m, Cost = 20_000m },
                    new ForecastBaselineLine { PhaseId = 2, ResourceTypeId = 2, Location = "Onshore", Hours = 100m, HourlyRate = 100m, Cost = 10_000m }
                ]
            });
        }

        return i;
    }

    private static ActualEntry Entry(DateOnly date, decimal hours, decimal? calculated, int typeId = 1, decimal? sourced = null, bool unmapped = false) => new()
    {
        InitiativeId = 5, ExternalProjectId = "P", SourceReference = Guid.NewGuid().ToString(), WorkDate = date, Hours = hours,
        CalculatedCost = calculated, SourcedCost = sourced, IsUnmapped = unmapped,
        PersonId = unmapped ? null : 1,
        Person = unmapped ? null : new Person { Id = 1, DisplayName = "p", ResourceTypeId = typeId, Location = "Onshore" }
    };

    [Fact]
    public void Totals_combine_sourced_actuals_and_adjustments_against_current_baseline()
    {
        var entries = new List<ActualEntry>
        {
            Entry(new DateOnly(2026, 3, 5), 100m, 10_000m),
            Entry(new DateOnly(2026, 3, 6), 50m, null, sourced: 6_000m),
            Entry(new DateOnly(2026, 4, 2), 40m, 4_000m, typeId: 2),
            Entry(new DateOnly(2026, 5, 1), 10m, null),
            Entry(new DateOnly(2026, 3, 7), 999m, 99_999m, unmapped: true)
        };
        var adjustments = new List<ActualAdjustment> { new() { InitiativeId = 5, Hours = 10m, Cost = 2_000m, Reason = "Invoice", CreatedBy = "u" } };

        var v = VarianceCalculator.Calculate(Initiative(threshold: 5m), entries, adjustments, Types);

        Assert.Equal(200m, v.SourcedHours);
        Assert.Equal(20_000m, v.SourcedCost);
        Assert.Equal(210m, v.ActualHours);
        Assert.Equal(22_000m, v.ActualCost);
        Assert.Equal(300m, v.BaselineHours);
        Assert.Equal(30_000m, v.BaselineCost);
        Assert.Equal(-8_000m, v.CostVariance);
        Assert.Equal(-26.7m, v.CostVariancePct);
        Assert.Equal(1, v.UnpricedEntries);
        Assert.False(v.ExceedsThreshold);

        var build = Assert.Single(v.ByPhase, r => r.Label == "Build");
        Assert.Equal(150m, build.ActualHours);
        Assert.Equal(16_000m, build.ActualCost);
        Assert.Equal(20_000m, build.BaselineCost);
        var outside = Assert.Single(v.ByPhase, r => r.Label == VarianceCalculator.OutsidePhases);
        Assert.Equal(10m, outside.ActualHours);
        Assert.Equal(0m, outside.BaselineHours);
        Assert.Equal(["Build", "Launch", VarianceCalculator.OutsidePhases], v.ByPhase.Select(r => r.Label));

        var qa = Assert.Single(v.ByResourceType, r => r.Label == "QA");
        Assert.Equal(40m, qa.ActualHours);
        Assert.Equal(-6_000m, qa.CostVariance);
    }

    [Fact]
    public void Threshold_breach_uses_initiative_setting_over_default()
    {
        var entries = new List<ActualEntry> { Entry(new DateOnly(2026, 3, 5), 350m, 35_000m) };

        var over = VarianceCalculator.Calculate(Initiative(threshold: 10m), entries, [], Types, defaultThresholdPct: 50m);
        Assert.Equal(16.7m, over.CostVariancePct);
        Assert.True(over.ExceedsThreshold);

        var under = VarianceCalculator.Calculate(Initiative(), entries, [], Types, defaultThresholdPct: 50m);
        Assert.Equal(50m, under.ThresholdPct);
        Assert.False(under.ExceedsThreshold);

        Assert.False(VarianceCalculator.Calculate(Initiative(), entries, [], Types).ExceedsThreshold);
    }

    [Fact]
    public void No_baseline_gives_zero_baseline_and_undefined_percentage()
    {
        var v = VarianceCalculator.Calculate(Initiative(withBaseline: false), [Entry(new DateOnly(2026, 3, 5), 8m, 800m)], [], Types, 10m);

        Assert.Null(v.Baseline);
        Assert.Equal(0m, v.BaselineCost);
        Assert.Equal(800m, v.CostVariance);
        Assert.Null(v.CostVariancePct);
        Assert.False(v.ExceedsThreshold);
    }
}
