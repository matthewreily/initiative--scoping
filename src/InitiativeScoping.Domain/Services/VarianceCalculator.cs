using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record VarianceRow(string Label, decimal BaselineHours, decimal BaselineCost, decimal ActualHours, decimal ActualCost)
{
    public decimal HoursVariance => ActualHours - BaselineHours;
    public decimal CostVariance => ActualCost - BaselineCost;
    public decimal? CostVariancePct => BaselineCost == 0 ? null : Math.Round(CostVariance / BaselineCost * 100m, 1);
}

/// <summary>Spent-to-date (sourced actuals + manual adjustments) versus the current baseline. No EAC/ETC in v1.</summary>
public sealed record VarianceResult(
    ForecastBaseline? Baseline,
    decimal SourcedHours,
    decimal SourcedCost,
    decimal AdjustmentHours,
    decimal AdjustmentCost,
    int UnpricedEntries,
    IReadOnlyList<VarianceRow> ByPhase,
    IReadOnlyList<VarianceRow> ByResourceType,
    decimal? ThresholdPct)
{
    public decimal ActualHours => SourcedHours + AdjustmentHours;
    public decimal ActualCost => SourcedCost + AdjustmentCost;
    public decimal BaselineHours => Baseline?.TotalHours ?? 0m;
    public decimal BaselineCost => Baseline?.TotalCost ?? 0m;
    public decimal HoursVariance => ActualHours - BaselineHours;
    public decimal CostVariance => ActualCost - BaselineCost;
    public decimal? CostVariancePct => BaselineCost == 0 ? null : Math.Round(CostVariance / BaselineCost * 100m, 1);
    public bool ExceedsThreshold => ThresholdPct is not null && CostVariancePct is not null && CostVariancePct > ThresholdPct;
}

public static class VarianceCalculator
{
    public const string OutsidePhases = "Outside planned phases";
    private const string Unknown = "Unmapped person";

    public static VarianceResult Calculate(
        Initiative initiative,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments,
        IReadOnlyDictionary<int, string> resourceTypeNames,
        decimal? defaultThresholdPct = null)
    {
        var baseline = initiative.CurrentBaseline;
        var mapped = entries.Where(e => !e.IsUnmapped && e.InitiativeId == initiative.Id).ToList();
        var phases = initiative.Phases.OrderBy(p => p.Sequence).ThenBy(p => p.PlannedStart).ToList();
        var phaseNames = phases.ToDictionary(p => p.Id, p => p.Name);

        string PhaseFor(DateOnly date) =>
            phases.FirstOrDefault(p => date >= p.PlannedStart && date <= p.PlannedEnd)?.Name ?? OutsidePhases;

        var byPhase = Rows(
            phases.Select(p => p.Name).Append(OutsidePhases),
            baseline?.Lines.GroupBy(l => phaseNames.GetValueOrDefault(l.PhaseId, OutsidePhases)) ?? [],
            mapped.GroupBy(e => PhaseFor(e.WorkDate)));

        var byType = Rows(
            [],
            baseline?.Lines.GroupBy(l => resourceTypeNames.GetValueOrDefault(l.ResourceTypeId, "?")) ?? [],
            mapped.GroupBy(e => e.Person is null ? Unknown : resourceTypeNames.GetValueOrDefault(e.Person.ResourceTypeId, "?")));

        return new VarianceResult(
            baseline,
            mapped.Sum(e => e.Hours),
            mapped.Sum(e => e.EffectiveCost ?? 0m),
            adjustments.Sum(a => a.Hours),
            adjustments.Sum(a => a.Cost),
            mapped.Count(e => e.EffectiveCost is null),
            byPhase,
            byType,
            initiative.VarianceThresholdPct ?? defaultThresholdPct);
    }

    private static List<VarianceRow> Rows(
        IEnumerable<string> order,
        IEnumerable<IGrouping<string, ForecastBaselineLine>> baseline,
        IEnumerable<IGrouping<string, ActualEntry>> actuals)
    {
        var b = baseline.ToDictionary(g => g.Key, g => (Hours: g.Sum(l => l.Hours), Cost: g.Sum(l => l.Cost)));
        var a = actuals.ToDictionary(g => g.Key, g => (Hours: g.Sum(e => e.Hours), Cost: g.Sum(e => e.EffectiveCost ?? 0m)));
        var labels = order.Concat(b.Keys).Concat(a.Keys).Distinct().ToList();
        return labels
            .Where(l => b.ContainsKey(l) || a.ContainsKey(l))
            .Select(l => new VarianceRow(l,
                b.GetValueOrDefault(l).Hours, b.GetValueOrDefault(l).Cost,
                a.GetValueOrDefault(l).Hours, a.GetValueOrDefault(l).Cost))
            .ToList();
    }
}
