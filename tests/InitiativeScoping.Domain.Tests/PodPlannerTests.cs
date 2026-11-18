using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class PodPlannerTests
{
    private static readonly Phase Build = new() { Id = 1, Name = "Build", PlannedStart = new(2026, 1, 5), PlannedEnd = new(2026, 1, 9) };
    private static readonly IReadOnlySet<DateOnly> Holidays = new HashSet<DateOnly> { new(2026, 1, 6) };
    private static Person Member(int id, bool active = true) => new() { Id = id, DisplayName = $"Person {id}", IsActive = active, ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = 1, ResourcingClass = new() { Name = "Internal", DefaultCapexPercent = 70 } };
    private static Pod Team(params Person[] people) => new() { Id = 2, Name = "Team", BusinessUnitId = 7, Members = people.Select(p => new PodMember { PersonId = p.Id, Person = p }).ToList() };
    private static List<RateCard> Cards() => [new() { Name = "Rates", Status = RateCardStatus.Published, EffectiveStart = new(2026, 1, 1), Entries = [new() { ResourceTypeId = 1, SeniorityId = 3, Location = "Onshore", ResourcingClassId = 1, HourlyRate = 100 }] }];

    [Fact]
    public void Expansion_uses_active_members_pod_business_unit_calendar_and_class_defaults()
    {
        var plan = PodPlanner.Assign(9, Team(Member(1), Member(2, false)), [Build], 50, Cards(), Holidays, 8);
        var seat = Assert.Single(plan.Add);
        Assert.Equal(7, seat.BusinessUnitId);
        Assert.Equal(2, seat.PodId);
        Assert.Equal(1, seat.Quantity);
        Assert.Equal(1, Assert.Single(seat.People).PersonId);
        Assert.Equal(16, seat.EstimatedHours);
        Assert.Equal(70, seat.CapexPercent);
        Assert.Equal(50, seat.AllocationPercent);
        Assert.Empty(plan.Skipped);
    }

    [Fact]
    public void Empty_inputs_and_unpriced_members_do_not_create_seats()
    {
        Assert.Empty(PodPlanner.Assign(9, Team(), [Build], 100, Cards(), Holidays, 8).Add);
        Assert.Empty(PodPlanner.Assign(9, Team(Member(1)), [], 100, Cards(), Holidays, 8).Add);
        var plan = PodPlanner.Assign(9, Team(Member(1)), [Build], 100, [], Holidays, 8);
        Assert.Empty(plan.Add);
        Assert.Contains("Person 1 (Build): no priced rate", plan.Skipped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_percent_is_rejected(decimal percent) => Assert.Throws<ArgumentOutOfRangeException>(() => PodPlanner.Assign(9, Team(), [Build], percent, Cards(), Holidays, 8));

    [Fact]
    public void Resync_adds_joiners_removes_leavers_and_preserves_manual_edits()
    {
        var original = PodPlanner.Assign(9, Team(Member(1), Member(2)), [Build], 50, Cards(), Holidays, 8).Add.ToList();
        original[0].Id = 1; original[1].Id = 2;
        original[0].EstimatedHours = 123;
        original[0].CapexPercent = 12;
        var plan = PodPlanner.Resync(9, Team(Member(1), Member(3)), [Build], original, Cards(), Holidays, 8);
        Assert.Same(original[1], Assert.Single(plan.Remove));
        var added = Assert.Single(plan.Add);
        Assert.Equal(3, added.PersonIds.Single());
        Assert.Equal(50, added.AllocationPercent);
        Assert.Equal(123, original[0].EstimatedHours);
        Assert.Equal(12, original[0].CapexPercent);
        Assert.Empty(PodPlanner.Resync(9, Team(Member(1)), [Build], [], Cards(), Holidays, 8).Add);
        Assert.Equal(2, PodPlanner.Resync(9, Team(Member(1, false), Member(2, false)), [Build], original, Cards(), Holidays, 8).Remove.Count);
    }
}
