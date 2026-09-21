using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class ScenarioPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static RateCard Card() => new()
    {
        Name = "2026", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
        Entries =
        [
            new RateCardEntry { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId, HourlyRate = 100m },
            new RateCardEntry { ResourceTypeId = 2, SeniorityId = 3, Location = "Offshore", ResourcingClassId = ResourcingClass.VendorId, HourlyRate = 50m }
        ]
    };

    private static Initiative Live() => new()
    {
        Id = 7, Name = "Live", Description = "desc", BusinessUnitId = 1, SponsoringTeam = "Core", CreatedBy = "owner", CreatedAt = Now.AddMonths(-1),
        Status = InitiativeStatus.Draft, SizingMethod = SizingMethod.TShirt, SizeKey = "M", PlanningMode = PlanningMode.FixedDuration,
        TargetStart = new DateOnly(2026, 3, 1), TargetEnd = new DateOnly(2026, 4, 30), VarianceThresholdPct = 10m, ContingencyPct = 5m,
        EstimateConfidence = EstimateConfidence.Medium,
        ParticipatingBusinessUnits = [new InitiativeBusinessUnit { InitiativeId = 7, BusinessUnitId = 2 }],
        Members = [new InitiativeMember { InitiativeId = 7, UserId = "owner", Role = InitiativeMemberRole.Owner }],
        Phases =
        [
            new Phase { Id = 10, InitiativeId = 7, Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 1), PlannedEnd = new DateOnly(2026, 3, 31) },
            new Phase { Id = 11, InitiativeId = 7, Name = "Test", Sequence = 2, PlannedStart = new DateOnly(2026, 4, 1), PlannedEnd = new DateOnly(2026, 4, 30) }
        ],
        Allocations =
        [
            new InitiativeAllocation { Id = 100, InitiativeId = 7, PhaseId = 10, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId, ResourcingClass = TestClasses.Internal, Quantity = 2, EstimatedHours = 100, AllocationPercent = 50, CostCenter = "CC1" },
            new InitiativeAllocation { Id = 101, InitiativeId = 7, PhaseId = 11, BusinessUnitId = 2, ResourceTypeId = 2, SeniorityId = 3, Location = "Offshore", ResourcingClassId = ResourcingClass.VendorId, ResourcingClass = TestClasses.Vendor, VendorId = 9, Quantity = 1, EstimatedHours = 80, ContractReference = "PO-1" }
        ],
        NonLaborCosts =
        [
            new InitiativeNonLaborCost { Id = 200, InitiativeId = 7, PhaseId = 11, Description = "License", BillingModel = BillingModel.OneTime, Quantity = 1, UnitCost = 1_000m },
            new InitiativeNonLaborCost { Id = 201, InitiativeId = 7, Description = "Hosting", BillingModel = BillingModel.Monthly, Quantity = 1, UnitCost = 100m, StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 4, 30) }
        ],
        Baselines = [new ForecastBaseline { Id = 1, InitiativeId = 7, Version = 1, SnapshotBy = "x", IsCurrent = true, TotalCost = 1m }],
        SourceMappings = [new InitiativeSourceMapping { Source = "Planview", ExternalProjectId = "P1" }]
    };

    [Fact]
    public void Clone_copies_plan_and_planning_fields_but_not_history()
    {
        var live = Live();
        var s = ScenarioPlanner.Clone(live, "Live — Scenario A", "alice", Now);

        Assert.Equal(0, s.Id);
        Assert.Equal(7, s.ScenarioOfId);
        Assert.True(s.IsScenario);
        Assert.Equal(InitiativeStatus.Draft, s.Status);
        Assert.Equal("alice", s.CreatedBy);
        Assert.Equal(Now, s.CreatedAt);
        Assert.Equal("desc", s.Description);
        Assert.Equal((1, "Core", SizingMethod.TShirt, "M", PlanningMode.FixedDuration), (s.BusinessUnitId, s.SponsoringTeam, s.SizingMethod, s.SizeKey, s.PlanningMode));
        Assert.Equal((live.TargetStart, live.TargetEnd, 10m, 5m, EstimateConfidence.Medium), (s.TargetStart, s.TargetEnd, s.VarianceThresholdPct, s.ContingencyPct, s.EstimateConfidence));
        Assert.Equal([2], s.ParticipatingBusinessUnits.Select(p => p.BusinessUnitId));
        Assert.Equal([("owner", InitiativeMemberRole.Owner)], s.Members.Select(m => (m.UserId, m.Role)));

        Assert.Equal(["Build", "Test"], s.Phases.Select(p => p.Name));
        Assert.All(s.Phases, p => Assert.Equal(0, p.Id));
        Assert.Equal(2, s.Allocations.Count);
        Assert.All(s.Allocations, a => Assert.Equal(0, a.Id));
        Assert.Same(s.Phases[0], s.Allocations[0].Phase);
        Assert.Same(s.Phases[1], s.Allocations[1].Phase);
        Assert.Equal((2, 100m, 50m, "CC1"), (s.Allocations[0].Quantity, s.Allocations[0].EstimatedHours, s.Allocations[0].AllocationPercent, s.Allocations[0].CostCenter));
        Assert.Equal((9, "PO-1", ResourcingClass.VendorId), (s.Allocations[1].VendorId, s.Allocations[1].ContractReference, s.Allocations[1].ResourcingClassId));
        Assert.Same(s.Phases[1], s.NonLaborCosts[0].Phase);
        Assert.Null(s.NonLaborCosts[1].Phase);
        Assert.Equal(100m, s.NonLaborCosts[1].UnitCost);

        Assert.Empty(s.Baselines);
        Assert.Empty(s.SourceMappings);
        Assert.Empty(s.RebaselineRequests);

        // Source untouched.
        Assert.Equal(2, live.Allocations.Count);
        Assert.Equal(10, live.Allocations[0].PhaseId);
    }

    [Fact]
    public void Cloning_a_scenario_makes_a_sibling_not_a_grandchild()
    {
        var a = ScenarioPlanner.Clone(Live(), "A", "u", Now);
        a.Id = 50;
        var b = ScenarioPlanner.Clone(a, "B", "u", Now);
        Assert.Equal(7, b.ScenarioOfId);
    }

    [Fact]
    public void Clone_forecasts_the_same_as_its_source()
    {
        var live = Live();
        var s = ScenarioPlanner.Clone(live, "A", "u", Now);
        var cards = new[] { Card() };
        var before = ForecastCalculator.Calculate(live, cards);
        var after = ForecastCalculator.Calculate(s, cards);
        Assert.Equal(before.TotalHours, after.TotalHours);
        Assert.Equal(before.TotalCost, after.TotalCost);
        Assert.Equal(before.TotalCostWithContingency, after.TotalCostWithContingency);
    }

    [Fact]
    public void Promote_replaces_plan_and_planning_fields_but_keeps_identity_members_baselines_and_mappings()
    {
        var live = Live();
        var scenario = ScenarioPlanner.Clone(live, "A", "u", Now);
        scenario.Id = 50;
        scenario.Phases[0].Id = 60; scenario.Phases[1].Id = 61;
        foreach (var a in scenario.Allocations) { a.PhaseId = a.Phase!.Id; }
        foreach (var c in scenario.NonLaborCosts) { c.PhaseId = c.Phase?.Id; }
        scenario.Allocations.RemoveAt(1);
        scenario.Allocations[0].Quantity = 3;
        scenario.NonLaborCosts.Clear();
        scenario.PlanningMode = PlanningMode.EffortDriven;
        scenario.TargetEnd = null;
        scenario.ContingencyPct = 20m;
        scenario.EstimateConfidence = EstimateConfidence.High;
        scenario.Description = "leaner";
        scenario.BusinessUnitId = 9;
        scenario.ParticipatingBusinessUnits.Add(new InitiativeBusinessUnit { BusinessUnitId = 9 });
        scenario.Members.Add(new InitiativeMember { UserId = "bob", Role = InitiativeMemberRole.Contributor });

        var plan = ScenarioPlanner.Promote(scenario, live);

        Assert.Equal(7, live.Id);
        Assert.Equal("Live", live.Name);
        Assert.Null(live.ScenarioOfId);
        Assert.Equal("leaner", live.Description);
        Assert.Equal((PlanningMode.EffortDriven, (DateOnly?)null, 20m, EstimateConfidence.High), (live.PlanningMode, live.TargetEnd, live.ContingencyPct, live.EstimateConfidence));
        Assert.Same(plan.Phases, live.Phases);
        Assert.Same(plan.Allocations, live.Allocations);
        Assert.Same(plan.NonLaborCosts, live.NonLaborCosts);
        Assert.Equal(2, live.Phases.Count);
        Assert.All(live.Phases, p => Assert.Equal(0, p.Id));
        Assert.Single(live.Allocations);
        Assert.Equal(3, live.Allocations[0].Quantity);
        Assert.Same(live.Phases[0], live.Allocations[0].Phase);
        Assert.Empty(live.NonLaborCosts);
        Assert.Equal(9, live.BusinessUnitId);
        Assert.Contains(live.ParticipatingBusinessUnits, p => p.BusinessUnitId == 9);

        Assert.Single(live.Members);
        Assert.Single(live.Baselines);
        Assert.Single(live.SourceMappings);
        // The scenario's own graph is left alone for the caller to remove.
        Assert.Equal(2, scenario.Phases.Count);
        Assert.Equal(60, scenario.Phases[0].Id);
    }

    [Fact]
    public void Scenarios_cannot_be_activated()
    {
        var live = Live();
        var scenario = ScenarioPlanner.Clone(live, "A", "u", Now);
        var cards = new[] { Card() };
        Assert.Empty(InitiativeLifecycle.BaselineBlockers(live, ForecastCalculator.Calculate(live, cards)));
        Assert.Contains(InitiativeLifecycle.BaselineBlockers(scenario, ForecastCalculator.Calculate(scenario, cards)), b => b.Contains("promote"));
    }

    [Fact]
    public void Comparison_puts_live_plan_first_and_exposes_deltas_per_column()
    {
        var live = Live();
        var scenario = ScenarioPlanner.Clone(live, "A", "u", Now);
        scenario.Id = 50;
        scenario.Allocations[0].Quantity = 1;
        var cmp = ScenarioComparison.Build(live, [scenario], [Card()]);

        Assert.True(cmp.Parent.IsParent);
        Assert.Same(live, cmp.Parent.Initiative);
        var col = Assert.Single(cmp.Scenarios);
        Assert.Equal(280m, cmp.Parent.Hours);
        Assert.Equal(180m, col.Hours);
        Assert.Equal(200m, cmp.Parent.InternalHours);
        Assert.Equal(80m, col.VendorHours);
        Assert.Equal(4_000m, col.VendorCost);
        Assert.Equal(3, cmp.Parent.HeadCount);
        Assert.Equal(2, col.HeadCount);
        Assert.Equal([1, 2], cmp.ResourceTypeIds);
        Assert.Equal([(ResourcingClass.InternalId, "Internal", false), (ResourcingClass.VendorId, "Vendor", true)], cmp.Classes.Select(c => (c.Id, c.Name, c.IsVendor)));
        Assert.Equal(100m, col.HoursByResourceType(1));
        Assert.Equal(100m, col.HoursByClass(ResourcingClass.InternalId));
        Assert.Equal(80m, col.HoursByClass(ResourcingClass.VendorId));
        Assert.Equal(100m, col.HoursByResourceType(1, ResourcingClass.InternalId));
        Assert.Equal(0m, col.HoursByResourceType(1, ResourcingClass.VendorId));
        Assert.Equal(80m, col.HoursByResourceType(2, ResourcingClass.VendorId));
        Assert.Equal(new DateOnly(2026, 3, 1), col.PlanStart);
        Assert.Equal(new DateOnly(2026, 4, 30), col.PlanEnd);
        Assert.Equal(0, col.UnpricedLines);
    }

    [Fact]
    public void Comparison_splits_headcount_by_class_and_reports_peak_concurrent_seats()
    {
        var live = Live();
        var scenario = ScenarioPlanner.Clone(live, "A", "u", Now);
        scenario.Id = 50;
        // Overlap the phases so the internal Build seats and the vendor Test seat are staffed on the same day.
        scenario.Phases[1].PlannedStart = new DateOnly(2026, 3, 15);
        scenario.Allocations[1].Quantity = 3;
        var cmp = ScenarioComparison.Build(live, [scenario], [Card()]);
        var col = Assert.Single(cmp.Scenarios);

        Assert.Equal(2, cmp.Parent.HeadCountByClass(ResourcingClass.InternalId));
        Assert.Equal(1, cmp.Parent.HeadCountByClass(ResourcingClass.VendorId));
        Assert.Equal(2, cmp.Parent.PeakHeadCount); // Build (2) and Test (1) do not overlap
        Assert.Equal(1, cmp.Parent.PeakHeadCountByClass(ResourcingClass.VendorId));

        Assert.Equal(5, col.HeadCount);
        Assert.Equal(3, col.HeadCountByClass(ResourcingClass.VendorId));
        Assert.Equal(5, col.PeakHeadCount);
        Assert.Equal(2, col.PeakHeadCountByClass(ResourcingClass.InternalId));
        Assert.Equal(3, col.PeakHeadCountByClass(ResourcingClass.VendorId));
    }
}
