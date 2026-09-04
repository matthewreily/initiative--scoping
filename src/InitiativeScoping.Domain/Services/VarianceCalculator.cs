using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record VarianceRow(string Label, decimal BaselineHours, decimal BaselineCost, decimal ActualHours, decimal ActualCost, decimal EtcHours, decimal EtcCost)
{
    public decimal HoursVariance => ActualHours - BaselineHours;
    public decimal CostVariance => ActualCost - BaselineCost;
    public decimal? CostVariancePct => BaselineCost == 0 ? null : Math.Round(CostVariance / BaselineCost * 100m, 1);
    public decimal EacHours => ActualHours + EtcHours;
    public decimal EacCost => ActualCost + EtcCost;
    public decimal EacCostVariance => EacCost - BaselineCost;
    public decimal? EacCostVariancePct => BaselineCost == 0 ? null : Math.Round(EacCostVariance / BaselineCost * 100m, 1);
}

/// <summary>
/// Spent-to-date (sourced actuals + manual adjustments) versus the current baseline, plus a schedule-based projection:
/// ETC is the baseline cost of work not yet elapsed as of <see cref="AsOf"/> (future phases in full, the in-progress
/// phase pro-rated by remaining days, past phases zero); EAC = actual + ETC.
/// </summary>
public sealed record VarianceResult(
    ForecastBaseline? Baseline,
    DateOnly AsOf,
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
    public decimal EtcHours => ByPhase.Sum(r => r.EtcHours);
    public decimal EtcCost => ByPhase.Sum(r => r.EtcCost);
    public decimal EacHours => ActualHours + EtcHours;
    public decimal EacCost => ActualCost + EtcCost;
    public decimal EacCostVariance => EacCost - BaselineCost;
    public decimal? EacCostVariancePct => BaselineCost == 0 ? null : Math.Round(EacCostVariance / BaselineCost * 100m, 1);
    public bool EacExceedsThreshold => ThresholdPct is not null && EacCostVariancePct is not null && EacCostVariancePct > ThresholdPct;
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
        decimal? defaultThresholdPct = null,
        DateOnly? asOf = null)
    {
        var today = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var baseline = initiative.CurrentBaseline;
        var mapped = entries.Where(e => !e.IsUnmapped && e.InitiativeId == initiative.Id).ToList();
        var phases = initiative.Phases.OrderBy(p => p.Sequence).ThenBy(p => p.PlannedStart).ToList();
        var phaseNames = phases.ToDictionary(p => p.Id, p => p.Name);

        var remaining = phases.ToDictionary(p => p.Id, p => RemainingFraction(p, today));

        string PhaseFor(DateOnly date) =>
            phases.FirstOrDefault(p => date >= p.PlannedStart && date <= p.PlannedEnd)?.Name ?? OutsidePhases;

        decimal Remaining(ForecastBaselineLine l) => remaining.GetValueOrDefault(l.PhaseId, 0m);

        var byPhase = Rows(
            phases.Select(p => p.Name).Append(OutsidePhases),
            baseline?.Lines.GroupBy(l => phaseNames.GetValueOrDefault(l.PhaseId, OutsidePhases)) ?? [],
            mapped.GroupBy(e => PhaseFor(e.WorkDate)),
            Remaining);

        var byType = Rows(
            [],
            baseline?.Lines.GroupBy(l => resourceTypeNames.GetValueOrDefault(l.ResourceTypeId, "?")) ?? [],
            mapped.GroupBy(e => e.Person is null ? Unknown : resourceTypeNames.GetValueOrDefault(e.Person.ResourceTypeId, "?")),
            Remaining);

        return new VarianceResult(
            baseline,
            today,
            mapped.Sum(e => e.Hours),
            mapped.Sum(e => e.EffectiveCost ?? 0m),
            adjustments.Sum(a => a.Hours),
            adjustments.Sum(a => a.Cost),
            mapped.Count(e => e.EffectiveCost is null),
            byPhase,
            byType,
            initiative.VarianceThresholdPct ?? defaultThresholdPct);
    }

    /// <summary>Share of a phase's planned calendar days that fall after <paramref name="asOf"/> (1 = not started, 0 = finished).</summary>
    public static decimal RemainingFraction(Phase phase, DateOnly asOf)
    {
        if (asOf < phase.PlannedStart)
        {
            return 1m;
        }

        if (asOf >= phase.PlannedEnd)
        {
            return 0m;
        }

        var total = phase.PlannedEnd.DayNumber - phase.PlannedStart.DayNumber + 1;
        var left = phase.PlannedEnd.DayNumber - asOf.DayNumber;
        return Math.Round((decimal)left / total, 4);
    }

    private static List<VarianceRow> Rows(
        IEnumerable<string> order,
        IEnumerable<IGrouping<string, ForecastBaselineLine>> baseline,
        IEnumerable<IGrouping<string, ActualEntry>> actuals,
        Func<ForecastBaselineLine, decimal> remaining)
    {
        var b = baseline.ToDictionary(g => g.Key, g => (
            Hours: g.Sum(l => l.Hours),
            Cost: g.Sum(l => l.Cost),
            EtcHours: Math.Round(g.Sum(l => l.Hours * remaining(l)), 2),
            EtcCost: Math.Round(g.Sum(l => l.Cost * remaining(l)), 2)));
        var a = actuals.ToDictionary(g => g.Key, g => (Hours: g.Sum(e => e.Hours), Cost: g.Sum(e => e.EffectiveCost ?? 0m)));
        var labels = order.Concat(b.Keys).Concat(a.Keys).Distinct().ToList();
        return labels
            .Where(l => b.ContainsKey(l) || a.ContainsKey(l))
            .Select(l => new VarianceRow(l,
                b.GetValueOrDefault(l).Hours, b.GetValueOrDefault(l).Cost,
                a.GetValueOrDefault(l).Hours, a.GetValueOrDefault(l).Cost,
                b.GetValueOrDefault(l).EtcHours, b.GetValueOrDefault(l).EtcCost))
            .ToList();
    }
}
