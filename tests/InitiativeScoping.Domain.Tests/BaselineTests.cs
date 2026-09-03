using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class BaselineTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

    private static Initiative NewInitiative(int phases = 1, int allocations = 1)
    {
        var initiative = new Initiative { Id = 7, Name = "X", BusinessUnitId = 1, TargetStart = new DateOnly(2026, 3, 1), CreatedBy = "u" };
        for (var p = 1; p <= phases; p++)
        {
            initiative.Phases.Add(new Phase { Id = p, InitiativeId = 7, Name = $"P{p}", Sequence = p, PlannedStart = new DateOnly(2026, p, 1), PlannedEnd = new DateOnly(2026, p, 28) });
        }

        for (var a = 1; a <= allocations; a++)
        {
            initiative.Allocations.Add(new InitiativeAllocation
            {
                Id = a, InitiativeId = 7, PhaseId = 1, ResourceTypeId = a, Seniority = Seniority.Mid, Location = "Onshore",
                ResourcingClass = ResourcingClass.InternalFte, Quantity = 2, EstimatedHours = 50m
            });
        }

        return initiative;
    }

    private static ForecastResult Forecast(Initiative i, decimal? rate) =>
        new(i.Allocations.Select(a => new ForecastLine(a, a.Quantity * a.EstimatedHours, rate)).ToList());

    [Fact]
    public void Snapshot_freezes_lines_and_becomes_current_v1()
    {
        var initiative = NewInitiative(allocations: 2);

        var baseline = BaselineSnapshot.Create(initiative, Forecast(initiative, 100m), "alice", Now, "Activation");

        Assert.Equal(1, baseline.Version);
        Assert.True(baseline.IsCurrent);
        Assert.Equal(200m, baseline.TotalHours);
        Assert.Equal(20_000m, baseline.TotalCost);
        Assert.Equal(2, baseline.Lines.Count);
        Assert.All(baseline.Lines, l => Assert.Equal(100m, l.HourlyRate));
        Assert.Same(baseline, initiative.CurrentBaseline);
    }

    [Fact]
    public void Rebaseline_increments_version_and_demotes_previous_without_altering_it()
    {
        var initiative = NewInitiative();
        var v1 = BaselineSnapshot.Create(initiative, Forecast(initiative, 100m), "alice", Now, null);

        initiative.Allocations[0].EstimatedHours = 80m;
        var v2 = BaselineSnapshot.Create(initiative, Forecast(initiative, 110m), "bob", Now.AddDays(30), "Scope grew");

        Assert.Equal(2, v2.Version);
        Assert.True(v2.IsCurrent);
        Assert.False(v1.IsCurrent);
        Assert.Equal(100m, v1.TotalHours);
        Assert.Equal(10_000m, v1.TotalCost);
        Assert.Equal(160m, v2.TotalHours);
        Assert.Equal(17_600m, v2.TotalCost);
        Assert.Equal(2, initiative.Baselines.Count);
        Assert.Same(v2, initiative.CurrentBaseline);
    }

    [Fact]
    public void Snapshot_refuses_unpriced_forecast()
    {
        var initiative = NewInitiative();
        Assert.Throws<InvalidOperationException>(() => BaselineSnapshot.Create(initiative, Forecast(initiative, null), "a", Now, null));
    }

    [Fact]
    public void Blockers_report_missing_phase_allocation_and_unpriced_lines()
    {
        var empty = NewInitiative(phases: 0, allocations: 0);
        var blockers = InitiativeLifecycle.BaselineBlockers(empty, Forecast(empty, 100m));
        Assert.Contains(blockers, b => b.Contains("phase"));
        Assert.Contains(blockers, b => b.Contains("allocation"));

        var unpriced = NewInitiative();
        Assert.Single(InitiativeLifecycle.BaselineBlockers(unpriced, Forecast(unpriced, null)), b => b.Contains("no matching published rate"));

        var ok = NewInitiative();
        Assert.Empty(InitiativeLifecycle.BaselineBlockers(ok, Forecast(ok, 100m)));
    }

    [Theory]
    [InlineData(InitiativeStatus.Draft, InitiativeStatus.Active, true)]
    [InlineData(InitiativeStatus.Draft, InitiativeStatus.OnHold, false)]
    [InlineData(InitiativeStatus.Active, InitiativeStatus.OnHold, true)]
    [InlineData(InitiativeStatus.OnHold, InitiativeStatus.Active, true)]
    [InlineData(InitiativeStatus.Active, InitiativeStatus.Complete, true)]
    [InlineData(InitiativeStatus.Complete, InitiativeStatus.Active, false)]
    [InlineData(InitiativeStatus.Cancelled, InitiativeStatus.Draft, false)]
    public void Transitions_follow_lifecycle(InitiativeStatus from, InitiativeStatus to, bool allowed) =>
        Assert.Equal(allowed, InitiativeLifecycle.CanTransition(from, to));
}
