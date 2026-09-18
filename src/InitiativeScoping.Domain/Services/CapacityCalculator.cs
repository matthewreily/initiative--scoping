using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

/// <summary>Hours one initiative demands from a resource type in a month.</summary>
public sealed record CapacityContribution(Initiative Initiative, decimal Hours);

/// <summary>
/// One resource type in one month: planned demand (hours and FTE) against roster supply
/// (active people x working hours in the month).
/// </summary>
public sealed record CapacityCell(
    DateOnly Month,
    decimal DemandHours,
    decimal SupplyHours,
    int Headcount,
    decimal WorkingHoursPerPerson,
    IReadOnlyList<CapacityContribution> Contributions)
{
    public decimal DemandFte => WorkingHoursPerPerson <= 0 ? 0m : Math.Round(DemandHours / WorkingHoursPerPerson, 2, MidpointRounding.AwayFromZero);

    /// <summary>Demand / supply; null when nobody on the roster has this resource type.</summary>
    public decimal? Utilization => SupplyHours <= 0 ? null : Math.Round(DemandHours / SupplyHours, 4, MidpointRounding.AwayFromZero);

    public bool IsOverAllocated => Utilization > 1m || (SupplyHours <= 0 && DemandHours > 0);
}

public sealed record CapacityRow(int ResourceTypeId, string ResourceTypeName, int Headcount, IReadOnlyList<CapacityCell> Cells)
{
    /// <summary>Set in the person view; null for resource-type rows and for the "unassigned" rows of a type.</summary>
    public int? PersonId { get; init; }
    public string? PersonName { get; init; }
    /// <summary>Person-view row holding demand of allocations with no named person, grouped by resource type.</summary>
    public bool IsUnassigned { get; init; }
    public string Label => PersonName ?? (IsUnassigned ? $"Unassigned {ResourceTypeName}" : ResourceTypeName);
    public decimal DemandHours => Cells.Sum(c => c.DemandHours);
    public decimal PeakFte => Cells.Count == 0 ? 0m : Cells.Max(c => c.DemandFte);
    /// <summary>Unassigned demand is open staffing work, not an over-allocated person, so it is never flagged.</summary>
    public int OverAllocatedMonths => IsUnassigned ? 0 : Cells.Count(c => c.IsOverAllocated);
}

public sealed record CapacityHeatmap(IReadOnlyList<DateOnly> Months, IReadOnlyList<CapacityRow> Rows)
{
    public static readonly CapacityHeatmap Empty = new([], []);

    public bool IsEmpty => Rows.Count == 0;
    public decimal DemandHours => Rows.Sum(r => r.DemandHours);
    public int OverAllocatedCells => Rows.Sum(r => r.OverAllocatedMonths);

    public IReadOnlyList<CapacityCell> Totals =>
        Months.Select(m =>
        {
            var cells = Rows.Select(r => r.Cells.First(c => c.Month == m)).ToList();
            return new CapacityCell(m,
                cells.Sum(c => c.DemandHours),
                cells.Sum(c => c.SupplyHours),
                cells.Sum(c => c.Headcount),
                cells.Count == 0 ? 0m : cells[0].WorkingHoursPerPerson,
                []);
        }).ToList();
}

/// <summary>
/// Cross-initiative demand by month. Each allocation's hours are spread over its phase by calendar days (same rule
/// as <see cref="MonthlyPhasingCalculator"/>). Resource-type view: supply is the number of active roster people of
/// that type times the month's working hours (Mon–Fri minus holidays x hours/day). Person view: one row per named
/// person (supply = that person's working hours) plus an "unassigned" row per resource type (supply = 0, so any demand
/// is flagged) for allocations that have no person yet.
/// </summary>
public static class CapacityCalculator
{
    public static CapacityHeatmap Calculate(
        IReadOnlyList<Initiative> initiatives,
        IReadOnlyList<Person> people,
        IReadOnlyDictionary<int, string> resourceTypeNames,
        IReadOnlySet<DateOnly> holidays,
        decimal hoursPerDay)
    {
        var demand = Aggregate(initiatives, a => [(a.ResourceTypeId, a.Quantity * a.EstimatedHours)]);
        if (demand.Count == 0)
        {
            return CapacityHeatmap.Empty;
        }

        var months = MonthsOf(demand.Keys.Select(k => k.Month));
        var workingHours = months.ToDictionary(m => m, m => WorkingHoursInMonth(m, holidays, hoursPerDay));
        var headcount = people.Where(p => p.IsActive).GroupBy(p => p.ResourceTypeId).ToDictionary(g => g.Key, g => g.Count());
        var rows = demand.Keys.Select(k => k.Key).Distinct()
            .Select(type =>
            {
                var count = headcount.GetValueOrDefault(type);
                var cells = Cells(demand, type, months, m => count * workingHours[m], count, workingHours);
                return new CapacityRow(type, resourceTypeNames.GetValueOrDefault(type, $"Type {type}"), count, cells);
            })
            .OrderBy(r => r.ResourceTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CapacityHeatmap(months, rows);
    }

    public static CapacityHeatmap CalculateByPerson(
        IReadOnlyList<Initiative> initiatives,
        IReadOnlyList<Person> people,
        IReadOnlyDictionary<int, string> resourceTypeNames,
        IReadOnlySet<DateOnly> holidays,
        decimal hoursPerDay)
    {
        // Each named person is one seat keyed on the person id; unnamed seats key on the negated resource type id.
        var demand = Aggregate(initiatives, a => a.PersonIds.Select(p => (p, a.EstimatedHours)).Append((-a.ResourceTypeId, a.UnassignedSeats * a.EstimatedHours)));
        if (demand.Count == 0)
        {
            return CapacityHeatmap.Empty;
        }

        var months = MonthsOf(demand.Keys.Select(k => k.Month));
        var workingHours = months.ToDictionary(m => m, m => WorkingHoursInMonth(m, holidays, hoursPerDay));
        var byId = people.ToDictionary(p => p.Id);
        var personRows = new List<CapacityRow>();
        var unassignedRows = new List<CapacityRow>();
        foreach (var key in demand.Keys.Select(k => k.Key).Distinct())
        {
            if (key > 0)
            {
                byId.TryGetValue(key, out var person);
                var active = person?.IsActive == true;
                var typeId = person?.ResourceTypeId ?? 0;
                var cells = Cells(demand, key, months, m => active ? workingHours[m] : 0m, active ? 1 : 0, workingHours);
                personRows.Add(new CapacityRow(typeId, resourceTypeNames.GetValueOrDefault(typeId, $"Type {typeId}"), active ? 1 : 0, cells)
                {
                    PersonId = key,
                    PersonName = person?.DisplayName ?? $"Person #{key}"
                });
            }
            else
            {
                var typeId = -key;
                var cells = Cells(demand, key, months, _ => 0m, 0, workingHours);
                unassignedRows.Add(new CapacityRow(typeId, resourceTypeNames.GetValueOrDefault(typeId, $"Type {typeId}"), 0, cells) { IsUnassigned = true });
            }
        }

        var rows = personRows.OrderBy(r => r.PersonName, StringComparer.OrdinalIgnoreCase)
            .Concat(unassignedRows.OrderBy(r => r.ResourceTypeName, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new CapacityHeatmap(months, rows);
    }

    private static Dictionary<(int Key, DateOnly Month), Dictionary<int, (Initiative Initiative, decimal Hours)>> Aggregate(
        IReadOnlyList<Initiative> initiatives,
        Func<InitiativeAllocation, IEnumerable<(int Key, decimal Hours)>> seatsOf)
    {
        var demand = new Dictionary<(int Key, DateOnly Month), Dictionary<int, (Initiative Initiative, decimal Hours)>>();
        foreach (var initiative in initiatives)
        {
            var phases = initiative.Phases.ToDictionary(p => p.Id);
            foreach (var a in initiative.Allocations)
            {
                if (!phases.TryGetValue(a.PhaseId, out var phase) || phase.PlannedEnd < phase.PlannedStart)
                {
                    continue;
                }

                foreach (var (key, hours) in seatsOf(a))
                {
                    if (hours <= 0)
                    {
                        continue;
                    }

                    foreach (var (month, amount) in MonthlyPhasingCalculator.SpreadByDays(hours, phase.PlannedStart, phase.PlannedEnd))
                    {
                        if (!demand.TryGetValue((key, month), out var byInitiative))
                        {
                            byInitiative = [];
                            demand[(key, month)] = byInitiative;
                        }

                        var existing = byInitiative.TryGetValue(initiative.Id, out var c) ? c.Hours : 0m;
                        byInitiative[initiative.Id] = (initiative, existing + amount);
                    }
                }
            }
        }

        return demand;
    }

    private static List<DateOnly> MonthsOf(IEnumerable<DateOnly> demanded)
    {
        var list = demanded.ToList();
        var first = list.Min();
        var last = list.Max();
        var months = new List<DateOnly>();
        for (var m = first; m <= last; m = m.AddMonths(1))
        {
            months.Add(m);
        }

        return months;
    }

    private static List<CapacityCell> Cells(
        Dictionary<(int Key, DateOnly Month), Dictionary<int, (Initiative Initiative, decimal Hours)>> demand,
        int key,
        IReadOnlyList<DateOnly> months,
        Func<DateOnly, decimal> supplyOf,
        int headcount,
        IReadOnlyDictionary<DateOnly, decimal> workingHours) =>
        months.Select(m =>
        {
            var contributions = demand.TryGetValue((key, m), out var byInitiative)
                ? byInitiative.Values.OrderByDescending(v => v.Hours).Select(v => new CapacityContribution(v.Initiative, v.Hours)).ToList()
                : [];
            return new CapacityCell(m, contributions.Sum(c => c.Hours), supplyOf(m), headcount, workingHours[m], contributions);
        }).ToList();

    public static decimal WorkingHoursInMonth(DateOnly month, IReadOnlySet<DateOnly> holidays, decimal hoursPerDay)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        return DurationCalculator.WorkingDays(start, start.AddMonths(1).AddDays(-1), holidays) * hoursPerDay;
    }
}
