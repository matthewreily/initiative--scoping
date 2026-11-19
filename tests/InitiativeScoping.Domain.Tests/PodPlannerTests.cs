using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class PodPlannerTests
{
    [Theory]
    [InlineData(0.01, 1, 0.0001)]
    [InlineData(33.33, 17.5, 5.83275)]
    [InlineData(100, 100, 100)]
    public void Membership_product_preserves_fractional_percent_and_bounds(decimal chosen, decimal membership, decimal effective)
    {
        var pod = Team(Member(1));
        pod.Members[0].MembershipPercent = membership;
        var seat = Assert.Single(PodPlanner.Assign(9, pod, [Build], chosen, Cards(), Holidays, 8).Add);
        Assert.Equal(effective, seat.AllocationPercent);
        Assert.Equal(effective, seat.PodWrittenAllocationPercent);
        Assert.Equal(DurationCalculator.Hours(effective, 4, 8), seat.EstimatedHours);
    }

    [Fact]
    public void Membership_split_is_applied_and_snapshotted_and_resync_updates_only_changed_membership()
    {
        var pod = Team(Member(1), Member(2));
        pod.Members[0].MembershipPercent = 40;
        var seats = PodPlanner.Assign(9, pod, [Build], 50, Cards(), Holidays, 8).Add;
        var seat = seats[0];
        Assert.Equal(20, seat.AllocationPercent);
        Assert.Equal(6.4m, seat.EstimatedHours);
        Assert.Equal(50, seat.PodAllocationPercent);
        Assert.Equal(40, seat.PodMembershipPercent);
        Assert.Equal(seat.AllocationPercent, seat.PodWrittenAllocationPercent);
        Assert.Equal(seat.EstimatedHours, seat.PodWrittenEstimatedHours);
        Assert.Empty(PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8).SeatsToUpdate);
        pod.Members[0].MembershipPercent = 60;
        var change = Assert.Single(PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8).SeatsToUpdate);
        Assert.False(change.HandEdited);
        Assert.Equal(30, change.AllocationPercent);
        Assert.Equal(9.6m, change.EstimatedHours);
        Assert.Equal(20, seat.AllocationPercent);
        change.Apply();
        Assert.Equal(30, seat.AllocationPercent);
        Assert.Equal(60, seat.PodMembershipPercent);
        Assert.Equal(9.6m, seat.PodWrittenEstimatedHours);
        Assert.Empty(PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8).SeatsToUpdate);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Resync_detects_manual_percent_or_hours_edits_and_keeps_the_recorded_chosen_percent(bool percentEdit, bool hoursEdit)
    {
        var pod = Team(Member(1));
        pod.Members[0].MembershipPercent = 40;
        var seats = PodPlanner.Assign(9, pod, [Build], 50, Cards(), Holidays, 8).Add;
        if (percentEdit) seats[0].AllocationPercent = 99;
        if (hoursEdit) seats[0].EstimatedHours = 99;
        pod.Members[0].MembershipPercent = 60;
        pod.Members.Add(new PodMember { Person = Member(2), PersonId = 2, MembershipPercent = 30 });
        var plan = PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8);
        var change = Assert.Single(plan.SeatsToUpdate);
        Assert.True(change.HandEdited);
        Assert.Equal(30, change.AllocationPercent);
        Assert.Equal(15, Assert.Single(plan.Add).AllocationPercent);
        change.Apply();
        pod.Members[0].MembershipPercent = 80;
        Assert.False(Assert.Single(PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8).SeatsToUpdate).HandEdited);
    }

    [Fact]
    public void Legacy_seats_are_conservatively_hand_edited_and_mixed_memberships_require_splitting()
    {
        var pod = Team(Member(1));
        var seats = PodPlanner.Assign(9, pod, [Build], 50, Cards(), Holidays, 8).Add;
        var seat = seats[0];
        seat.PodAllocationPercent = seat.PodMembershipPercent = seat.PodWrittenAllocationPercent = seat.PodWrittenEstimatedHours = null;
        pod.Members[0].MembershipPercent = 40;
        var change = Assert.Single(PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8).SeatsToUpdate);
        Assert.True(change.HandEdited);
        Assert.Equal(20, change.AllocationPercent);
        pod.Members.Add(new PodMember { Person = Member(2), PersonId = 2, MembershipPercent = 60 });
        seat.People.Add(new InitiativeAllocationPerson { PersonId = 2 });
        seat.Quantity = 2;
        var mixed = PodPlanner.Resync(9, pod, [Build], seats, Cards(), Holidays, 8);
        Assert.Empty(mixed.SeatsToUpdate);
        Assert.Contains("split this allocation", Assert.Single(mixed.Skipped));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Invalid_membership_percent_is_rejected(decimal percent)
    {
        var pod = Team(Member(1));
        pod.Members[0].MembershipPercent = percent;
        Assert.Throws<ArgumentOutOfRangeException>(() => PodPlanner.Assign(9, pod, [Build], 50, Cards(), Holidays, 8));
    }

    [Fact]
    public void Assign_skips_vendor_members_without_a_vendor_even_with_a_generic_rate()
    {
        var person = Member(1);
        person.ResourcingClassId = 2;
        person.ResourcingClass = new ResourcingClass { Name = "Vendor", IsVendor = true, DefaultCapexPercent = 100 };
        var cards = Cards();
        cards[0].Entries[0].ResourcingClassId = 2;
        var plan = PodPlanner.Assign(9, Team(person), [Build], 100, cards, Holidays, 8);
        Assert.Empty(plan.Add);
        Assert.Contains("select a vendor", Assert.Single(plan.Skipped));
        person.VendorId = 10;
        var valid = PodPlanner.Assign(9, Team(person), [Build], 100, cards, Holidays, 8);
        Assert.Equal(10, Assert.Single(valid.Add).VendorId);
        Assert.Empty(valid.Skipped);
    }

    [Fact]
    public void Resync_removes_only_departed_people_from_mixed_allocations()
    {
        var seats = PodPlanner.Assign(9, Team(Member(1)), [Build], 50, Cards(), Holidays, 8).Add.ToList();
        var allocation = seats.Single();
        allocation.Quantity = 3;
        allocation.People.Add(new InitiativeAllocationPerson { PersonId = 2 });
        allocation.People.Add(new InitiativeAllocationPerson { PersonId = 3 });
        allocation.EstimatedHours = 123;
        var plan = PodPlanner.Resync(9, Team(Member(1), Member(2, false)), [Build], seats, Cards(), Holidays, 8);
        Assert.Empty(plan.Remove);
        Assert.Empty(plan.Add);
        var update = Assert.Single(plan.Update);
        Assert.Same(allocation, update.Allocation);
        Assert.Equal(new[] { 2, 3 }, update.People.Select(p => p.PersonId));
        Assert.Equal(3, allocation.Quantity);
        Assert.Equal(123, allocation.EstimatedHours);
        Assert.Empty(PodPlanner.Resync(9, Team(Member(1), Member(2), Member(3)), [Build], seats, Cards(), Holidays, 8).Update);
        var allGone = PodPlanner.Resync(9, Team(), [Build], seats, Cards(), Holidays, 8);
        Assert.Same(allocation, Assert.Single(allGone.Remove));
        Assert.Empty(allGone.Update);
    }

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
