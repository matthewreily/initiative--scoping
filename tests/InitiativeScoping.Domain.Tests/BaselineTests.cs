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
                Id = a, InitiativeId = 7, PhaseId = 1, BusinessUnitId = 1, ResourceTypeId = a, SeniorityId = 2, Location = "Onshore",
                ResourcingClassId = ResourcingClass.InternalId, Quantity = 2, EstimatedHours = 50m
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
    public void Snapshot_captures_dimension_names_so_later_renames_do_not_relabel_history()
    {
        var initiative = NewInitiative();
        var allocation = initiative.Allocations[0];
        allocation.BusinessUnit = new BusinessUnit { Id = 1, Name = "Retail" };
        allocation.ResourceType = new ResourceType { Id = 1, Name = "Engineer", DisciplineId = 1 };
        allocation.Seniority = new SeniorityLevel { Id = 2, Name = "Mid" };
        allocation.ResourcingClassId = ResourcingClass.VendorId;
        allocation.VendorId = 9;
        allocation.Vendor = new Vendor { Id = 9, Name = "Acme" };

        var forecast = Forecast(initiative, 100m) with
        {
            NonLaborLines =
            [
                new NonLaborForecastLine(
                    new InitiativeNonLaborCost { PhaseId = initiative.Phases[0].Id, Description = "License", UnitCost = 10m },
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 1, 10m),
                new NonLaborForecastLine(
                    new InitiativeNonLaborCost { PhaseId = null, Description = "Hosting", UnitCost = 5m },
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 1, 5m)
            ]
        };
        var baseline = BaselineSnapshot.Create(initiative, forecast, "alice", Now, null);

        allocation.BusinessUnit.Name = "Retail Banking";
        allocation.ResourceType.Name = "Software Engineer";
        allocation.Seniority.Name = "Level 2";
        allocation.Vendor.Name = "Acme Corp";
        initiative.Phases[0].Name = "Discovery";

        var line = Assert.Single(baseline.Lines);
        Assert.Equal("P1", line.PhaseName);
        Assert.Equal("Retail", line.BusinessUnitName);
        Assert.Equal("Engineer", line.ResourceTypeName);
        Assert.Equal("Mid", line.SeniorityName);
        Assert.Equal("Acme", line.VendorName);
        Assert.Equal("P1", baseline.NonLaborLines.Single(l => l.Description == "License").PhaseName);
        Assert.Null(baseline.NonLaborLines.Single(l => l.Description == "Hosting").PhaseName);
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

    [Fact]
    public void Snapshot_captures_person_id_and_display_name_for_named_allocations()
    {
        var initiative = NewInitiative(allocations: 3);
        initiative.Allocations[0].People.Add(new InitiativeAllocationPerson { PersonId = 4, Person = new Person { Id = 4, DisplayName = "Jane Doe", ResourceTypeId = 1, SeniorityId = 2, Location = "Onshore", BusinessUnitId = 1 } });
        initiative.Allocations[1].People.Add(new InitiativeAllocationPerson { PersonId = 5 });

        var baseline = BaselineSnapshot.Create(initiative, Forecast(initiative, 100m), "alice", Now, "Activation");

        // Quantity is 2, so each named allocation also leaves one unnamed seat line.
        Assert.Equal(5, baseline.Lines.Count);
        var named = baseline.Lines.Where(l => l.PersonId != null).OrderBy(l => l.ResourceTypeId).ToList();
        Assert.Equal(4, named[0].PersonId);
        Assert.Equal("Jane Doe", named[0].PersonName);
        Assert.Equal(5, named[1].PersonId);
        Assert.Equal("Person #5", named[1].PersonName);
        Assert.Equal([1, 2, 3], baseline.Lines.Where(l => l.PersonId == null).Select(l => l.ResourceTypeId));
    }

    [Fact]
    public void Snapshot_splits_a_multi_seat_allocation_into_one_line_per_named_person_plus_the_unnamed_seats()
    {
        var initiative = NewInitiative(allocations: 1);
        var a = initiative.Allocations[0];
        a.Quantity = 3;
        a.People.Add(new InitiativeAllocationPerson { PersonId = 4, Person = new Person { Id = 4, DisplayName = "Jane", ResourceTypeId = 1, SeniorityId = 2, Location = "Onshore", BusinessUnitId = 1 } });
        a.People.Add(new InitiativeAllocationPerson { PersonId = 5, Person = new Person { Id = 5, DisplayName = "Bob", ResourceTypeId = 1, SeniorityId = 2, Location = "Onshore", BusinessUnitId = 1 } });
        var forecast = Forecast(initiative, 100m);
        var line = forecast.Lines[0];

        var baseline = BaselineSnapshot.Create(initiative, forecast, "alice", Now, "Activation");

        Assert.Equal(3, baseline.Lines.Count);
        Assert.Equal(["Jane", "Bob", null], baseline.Lines.Select(l => l.PersonName));
        Assert.All(baseline.Lines, l => Assert.Equal(Math.Round(line.Hours / 3, 2), l.Hours, 2));
        Assert.Equal(line.Hours, baseline.Lines.Sum(l => l.Hours));
        Assert.Equal(line.Cost, baseline.Lines.Sum(l => l.Cost));
        Assert.Equal(baseline.TotalHours, baseline.Lines.Sum(l => l.Hours));
    }
}
