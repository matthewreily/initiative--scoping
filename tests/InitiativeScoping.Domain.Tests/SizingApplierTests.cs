using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class SizingApplierTests
{
    private static AllocationTemplateLine Line(string phase, int type, decimal percent) =>
        new() { PhaseName = phase, ResourceTypeId = type, Seniority = Seniority.Mid, Percent = percent };

    [Fact]
    public void Splits_hours_by_percent_and_sums_exactly_to_total()
    {
        var lines = new[] { Line("Discovery", 1, 33.33m), Line("Build", 2, 33.33m), Line("Launch", 3, 33.34m) };

        var result = SizingApplier.Apply(100m, lines);

        Assert.Equal(3, result.Count);
        Assert.Equal(33.33m, result[0].Hours);
        Assert.Equal(33.33m, result[1].Hours);
        Assert.Equal(33.34m, result[2].Hours);
        Assert.Equal(100m, result.Sum(r => r.Hours));
    }

    [Fact]
    public void Last_line_absorbs_rounding_remainder()
    {
        var lines = new[] { Line("A", 1, 10m), Line("A", 2, 10m), Line("B", 3, 80m) };

        var result = SizingApplier.Apply(0.05m, lines);

        Assert.Equal(0.01m, result[0].Hours);
        Assert.Equal(0.01m, result[1].Hours);
        Assert.Equal(0.03m, result[2].Hours);
        Assert.Equal(0.05m, result.Sum(r => r.Hours));
    }

    [Fact]
    public void Empty_template_yields_no_allocations()
    {
        Assert.Empty(SizingApplier.Apply(480m, []));
    }

    [Fact]
    public void Phase_names_are_distinct_case_insensitive_in_template_order()
    {
        var lines = new[] { Line("Build", 1, 50), Line("Discovery", 2, 25), Line("build", 3, 25) };

        Assert.Equal(["Build", "Discovery"], SizingApplier.PhaseNames(lines));
    }
}
