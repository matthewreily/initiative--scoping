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
    public decimal DemandHours => Cells.Sum(c => c.DemandHours);
    public decimal PeakFte => Cells.Count == 0 ? 0m : Cells.Max(c => c.DemandFte);
    public int OverAllocatedMonths => Cells.Count(c => c.IsOverAllocated);
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
/// Cross-initiative demand by resource type and month. Each allocation's hours are spread over its phase by
/// calendar days (same rule as <see cref="MonthlyPhasingCalculator"/>); supply is the number of active roster
/// people of that resource type times the month's working hours (Mon–Fri minus holidays x hours/day).
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
        var demand = new Dictionary<(int Type, DateOnly Month), Dictionary<int, (Initiative Initiative, decimal Hours)>>();

        foreach (var initiative in initiatives)
        {
            var phases = initiative.Phases.ToDictionary(p => p.Id);
            foreach (var a in initiative.Allocations)
            {
                if (!phases.TryGetValue(a.PhaseId, out var phase) || phase.PlannedEnd < phase.PlannedStart)
                {
                    continue;
                }

                var hours = a.Quantity * a.EstimatedHours;
                if (hours <= 0)
                {
                    continue;
                }

                foreach (var (month, amount) in MonthlyPhasingCalculator.SpreadByDays(hours, phase.PlannedStart, phase.PlannedEnd))
                {
                    if (!demand.TryGetValue((a.ResourceTypeId, month), out var byInitiative))
                    {
                        byInitiative = [];
                        demand[(a.ResourceTypeId, month)] = byInitiative;
                    }

                    var existing = byInitiative.TryGetValue(initiative.Id, out var c) ? c.Hours : 0m;
                    byInitiative[initiative.Id] = (initiative, existing + amount);
                }
            }
        }

        if (demand.Count == 0)
        {
            return CapacityHeatmap.Empty;
        }

        var first = demand.Keys.Min(k => k.Month);
        var last = demand.Keys.Max(k => k.Month);
        var months = new List<DateOnly>();
        for (var m = first; m <= last; m = m.AddMonths(1))
        {
            months.Add(m);
        }

        var workingHours = months.ToDictionary(m => m, m => WorkingHoursInMonth(m, holidays, hoursPerDay));
        var headcount = people.Where(p => p.IsActive).GroupBy(p => p.ResourceTypeId).ToDictionary(g => g.Key, g => g.Count());

        var rows = demand.Keys.Select(k => k.Type).Distinct()
            .Select(type =>
            {
                var count = headcount.GetValueOrDefault(type);
                var cells = months.Select(m =>
                {
                    var contributions = demand.TryGetValue((type, m), out var byInitiative)
                        ? byInitiative.Values.OrderByDescending(v => v.Hours).Select(v => new CapacityContribution(v.Initiative, v.Hours)).ToList()
                        : [];
                    return new CapacityCell(m,
                        contributions.Sum(c => c.Hours),
                        count * workingHours[m],
                        count,
                        workingHours[m],
                        contributions);
                }).ToList();
                return new CapacityRow(type, resourceTypeNames.GetValueOrDefault(type, $"Type {type}"), count, cells);
            })
            .OrderBy(r => r.ResourceTypeName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new CapacityHeatmap(months, rows);
    }

    public static decimal WorkingHoursInMonth(DateOnly month, IReadOnlySet<DateOnly> holidays, decimal hoursPerDay)
    {
        var start = new DateOnly(month.Year, month.Month, 1);
        return DurationCalculator.WorkingDays(start, start.AddMonths(1).AddDays(-1), holidays) * hoursPerDay;
    }
}
