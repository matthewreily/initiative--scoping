using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class CapacityCalculatorTests
{
    private static readonly Dictionary<int, string> TypeNames = new() { [1] = "Developer", [2] = "QA" };
    private static readonly HashSet<DateOnly> NoHolidays = [];

    private static Initiative Make(int id, string name, DateOnly start, DateOnly end, params (int Type, int Qty, decimal Hours)[] allocations)
    {
        var i = new Initiative { Id = id, Name = name, BusinessUnitId = 1, CreatedBy = "t", TargetStart = start };
        i.Phases.Add(new Phase { Id = id * 10, Name = "Build", Sequence = 1, PlannedStart = start, PlannedEnd = end });
        foreach (var (type, qty, hours) in allocations)
        {
            i.Allocations.Add(new InitiativeAllocation
            {
                PhaseId = id * 10, BusinessUnitId = 1, ResourceTypeId = type, SeniorityId = 1, Location = "Onshore",
                ResourcingClassId = ResourcingClass.InternalId, Quantity = qty, EstimatedHours = hours
            });
        }

        return i;
    }

    private static Person Dev(bool active = true) => new()
    {
        DisplayName = "p", ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", BusinessUnitId = 1, IsActive = active
    };

    [Fact]
    public void Working_hours_in_month_exclude_weekends_and_holidays()
    {
        // Jan 2026 has 22 weekdays; New Year's Day is a Thursday.
        Assert.Equal(176m, CapacityCalculator.WorkingHoursInMonth(new DateOnly(2026, 1, 1), NoHolidays, 8m));
        Assert.Equal(168m, CapacityCalculator.WorkingHoursInMonth(new DateOnly(2026, 1, 1), new HashSet<DateOnly> { new(2026, 1, 1) }, 8m));
    }

    [Fact]
    public void Demand_is_spread_by_days_and_summed_across_initiatives()
    {
        // Both whole-month windows so each lands in a single bucket.
        var a = Make(1, "A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), (1, 2, 80m));
        var b = Make(2, "B", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), (1, 1, 100m), (2, 1, 40m));

        var map = CapacityCalculator.Calculate([a, b], [Dev(), Dev()], TypeNames, NoHolidays, 8m);

        Assert.Equal([new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)], map.Months);
        Assert.Equal(["Developer", "QA"], map.Rows.Select(r => r.ResourceTypeName));

        var dev = map.Rows[0];
        Assert.Equal(2, dev.Headcount);
        var jan = dev.Cells[0];
        // A: 160 h in Jan; B: 100 h over 59 days → 31/59 ≈ 52.54 in Jan.
        Assert.Equal(160m + 52.54m, jan.DemandHours);
        Assert.Equal(2 * 176m, jan.SupplyHours);
        Assert.Equal(2, jan.Contributions.Count);
        Assert.Equal("A", jan.Contributions[0].Initiative.Name);
        Assert.False(jan.IsOverAllocated);
        Assert.Equal(260m, dev.DemandHours);
        Assert.Equal(300m, map.DemandHours);
    }

    [Fact]
    public void Over_allocation_when_demand_exceeds_roster_or_roster_is_empty()
    {
        var a = Make(1, "A", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), (1, 3, 176m), (2, 1, 10m));

        var map = CapacityCalculator.Calculate([a], [Dev(), Dev(active: false)], TypeNames, NoHolidays, 8m);

        var dev = map.Rows[0].Cells[0];
        Assert.Equal(1, map.Rows[0].Headcount);
        Assert.Equal(3m, dev.DemandFte);
        Assert.Equal(3m, dev.Utilization);
        Assert.True(dev.IsOverAllocated);

        var qa = map.Rows[1].Cells[0];
        Assert.Null(qa.Utilization);
        Assert.True(qa.IsOverAllocated);
        Assert.Equal(2, map.OverAllocatedCells);
    }

    [Fact]
    public void Gap_months_are_filled_and_totals_roll_up()
    {
        var a = Make(1, "A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), (1, 1, 10m));
        var b = Make(2, "B", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), (1, 1, 20m));

        var map = CapacityCalculator.Calculate([a, b], [Dev()], TypeNames, NoHolidays, 8m);

        Assert.Equal(4, map.Months.Count);
        Assert.Equal([10m, 0m, 0m, 20m], map.Rows[0].Cells.Select(c => c.DemandHours));
        Assert.Equal([10m, 0m, 0m, 20m], map.Totals.Select(c => c.DemandHours));
        Assert.Equal(20m / 176m, map.Totals[3].Utilization!.Value, 3);
    }

    [Fact]
    public void Zero_hour_allocations_and_missing_phases_are_ignored()
    {
        var a = Make(1, "A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), (1, 1, 0m));
        a.Allocations.Add(new InitiativeAllocation { PhaseId = 999, BusinessUnitId = 1, ResourceTypeId = 1, SeniorityId = 1, Location = "x", Quantity = 1, EstimatedHours = 50 });

        Assert.True(CapacityCalculator.Calculate([a], [], TypeNames, NoHolidays, 8m).IsEmpty);
        Assert.True(CapacityCalculator.Calculate([], [], TypeNames, NoHolidays, 8m).IsEmpty);
    }

    [Fact]
    public void Person_view_shows_named_people_against_their_own_hours_and_groups_unassigned_by_type()
    {
        var a = Make(1, "A", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), (1, 1, 100m), (2, 1, 30m));
        var b = Make(2, "B", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), (1, 1, 120m), (1, 3, 40m));
        a.Allocations.First(x => x.ResourceTypeId == 1).People.Add(new InitiativeAllocationPerson { PersonId = 10 });
        b.Allocations.First(x => x.Quantity == 1).People.Add(new InitiativeAllocationPerson { PersonId = 10 });
        a.Allocations.First(x => x.ResourceTypeId == 2).People.Add(new InitiativeAllocationPerson { PersonId = 11 });
        var jane = new Person { Id = 10, DisplayName = "Jane", ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", BusinessUnitId = 1 };
        var gone = new Person { Id = 11, DisplayName = "Former", ResourceTypeId = 2, SeniorityId = 1, Location = "Onshore", BusinessUnitId = 1, IsActive = false };

        var map = CapacityCalculator.CalculateByPerson([a, b], [jane, gone], TypeNames, NoHolidays, 8m);

        Assert.Equal(["Former", "Jane", "Unassigned Developer"], map.Rows.Select(r => r.Label));

        var janeRow = map.Rows[1];
        Assert.Equal(10, janeRow.PersonId);
        Assert.Equal("Developer", janeRow.ResourceTypeName);
        Assert.Equal(1, janeRow.Headcount);
        // March 2026 has 22 weekdays → 176 h; 220 h demand from two initiatives.
        Assert.Equal(220m, janeRow.Cells[0].DemandHours);
        Assert.Equal(176m, janeRow.Cells[0].SupplyHours);
        Assert.True(janeRow.Cells[0].IsOverAllocated);
        Assert.Equal(2, janeRow.Cells[0].Contributions.Count);
        Assert.Equal(1, janeRow.OverAllocatedMonths);

        var former = map.Rows[0];
        Assert.Equal(0, former.Headcount);
        Assert.Equal(0m, former.Cells[0].SupplyHours);
        Assert.True(former.Cells[0].IsOverAllocated);

        var open = map.Rows[2];
        Assert.True(open.IsUnassigned);
        Assert.Null(open.PersonId);
        Assert.Equal(120m, open.Cells[0].DemandHours);
        Assert.Equal(0, open.OverAllocatedMonths);
    }

    [Fact]
    public void Person_view_labels_people_missing_from_the_roster_by_id()
    {
        var a = Make(1, "A", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), (1, 1, 10m));
        a.Allocations[0].People.Add(new InitiativeAllocationPerson { PersonId = 99 });

        var map = CapacityCalculator.CalculateByPerson([a], [], TypeNames, NoHolidays, 8m);

        Assert.Equal("Person #99", Assert.Single(map.Rows).Label);
        Assert.Equal(0, map.Rows[0].Headcount);
    }

    [Fact]
    public void Person_view_gives_each_named_person_one_seat_and_leaves_the_rest_unassigned()
    {
        // 3 seats × 100 h: Jane and Bob take one seat each, the third stays unassigned under the resource type.
        var a = Make(1, "A", new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), (1, 3, 100m));
        a.Allocations[0].People.Add(new InitiativeAllocationPerson { PersonId = 10 });
        a.Allocations[0].People.Add(new InitiativeAllocationPerson { PersonId = 12 });
        var jane = new Person { Id = 10, DisplayName = "Jane", ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", BusinessUnitId = 1 };
        var bob = new Person { Id = 12, DisplayName = "Bob", ResourceTypeId = 1, SeniorityId = 1, Location = "Onshore", BusinessUnitId = 1 };

        var map = CapacityCalculator.CalculateByPerson([a], [jane, bob], TypeNames, NoHolidays, 8m);

        Assert.Equal(["Bob", "Jane", "Unassigned Developer"], map.Rows.Select(r => r.Label));
        Assert.All(map.Rows, r => Assert.Equal(100m, r.Cells[0].DemandHours));
        Assert.Equal(300m, CapacityCalculator.Calculate([a], [jane, bob], TypeNames, NoHolidays, 8m).Rows[0].Cells[0].DemandHours);
    }
}
