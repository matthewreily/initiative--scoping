using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

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
/// phase pro-rated by remaining days, past phases zero; non-labor lines pro-rated over their own billing window); EAC = actual + ETC.
/// Non-labor rows appear in <see cref="ByPhase"/> with zero hours (initiative-wide lines under <see cref="VarianceCalculator.WholeInitiative"/>)
/// and per category in <see cref="ByCategory"/>, where manual adjustments are counted against their category.
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
    IReadOnlyList<VarianceRow> ByCategory,
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
    public const string WholeInitiative = "Whole initiative";
    private const string Unknown = "Unmapped person";

    private readonly record struct BaselineAmount(decimal Hours, decimal Cost, decimal Remaining);
    private readonly record struct ActualAmount(decimal Hours, decimal Cost);

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

        BaselineAmount Labor(ForecastBaselineLine l) => new(l.Hours, l.Cost, remaining.GetValueOrDefault(l.PhaseId, 0m));
        BaselineAmount NonLabor(ForecastBaselineNonLaborLine l) => new(0m, l.Cost, RemainingFraction(l.StartDate, l.EndDate, today));
        ActualAmount Sourced(ActualEntry e) => new(e.Hours, e.EffectiveCost ?? 0m);
        ActualAmount Adjusted(ActualAdjustment a) => new(a.Hours, a.Cost);

        var laborLines = baseline?.Lines ?? [];
        var nonLaborLines = baseline?.NonLaborLines ?? [];

        var byPhase = Rows(
            phases.Select(p => p.Name).Append(WholeInitiative).Append(OutsidePhases),
            laborLines.Select(l => (phaseNames.GetValueOrDefault(l.PhaseId, OutsidePhases), Labor(l)))
                .Concat(nonLaborLines.Select(l => (l.PhaseId is { } id ? phaseNames.GetValueOrDefault(id, OutsidePhases) : WholeInitiative, NonLabor(l)))),
            mapped.Select(e => (PhaseFor(e.WorkDate), Sourced(e))));

        var byType = Rows(
            [],
            laborLines.Select(l => (resourceTypeNames.GetValueOrDefault(l.ResourceTypeId, "?"), Labor(l))),
            mapped.Select(e => (e.Person is null ? Unknown : resourceTypeNames.GetValueOrDefault(e.Person.ResourceTypeId, "?"), Sourced(e))));

        var byCategory = Rows(
            Enum.GetValues<CostCategory>().Select(CategoryLabel),
            laborLines.Select(l => (CategoryLabel(CostCategory.Labor), Labor(l)))
                .Concat(nonLaborLines.Select(l => (CategoryLabel(l.Category), NonLabor(l)))),
            mapped.Select(e => (CategoryLabel(CostCategory.Labor), Sourced(e)))
                .Concat(adjustments.Select(a => (CategoryLabel(a.Category), Adjusted(a)))));

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
            byCategory,
            initiative.VarianceThresholdPct ?? defaultThresholdPct);
    }

    public static string CategoryLabel(CostCategory category) => category switch
    {
        CostCategory.SoftwareLicense => "Software license",
        _ => category.ToString()
    };

    /// <summary>Share of a phase's planned calendar days that fall after <paramref name="asOf"/> (1 = not started, 0 = finished).</summary>
    public static decimal RemainingFraction(Phase phase, DateOnly asOf) => RemainingFraction(phase.PlannedStart, phase.PlannedEnd, asOf);

    public static decimal RemainingFraction(DateOnly start, DateOnly end, DateOnly asOf)
    {
        if (asOf < start)
        {
            return 1m;
        }

        if (asOf >= end)
        {
            return 0m;
        }

        var total = end.DayNumber - start.DayNumber + 1;
        var left = end.DayNumber - asOf.DayNumber;
        return Math.Round((decimal)left / total, 4);
    }

    private static List<VarianceRow> Rows(
        IEnumerable<string> order,
        IEnumerable<(string Label, BaselineAmount Amount)> baseline,
        IEnumerable<(string Label, ActualAmount Amount)> actuals)
    {
        var b = baseline.GroupBy(x => x.Label).ToDictionary(g => g.Key, g => (
            Hours: g.Sum(x => x.Amount.Hours),
            Cost: g.Sum(x => x.Amount.Cost),
            EtcHours: Math.Round(g.Sum(x => x.Amount.Hours * x.Amount.Remaining), 2),
            EtcCost: Math.Round(g.Sum(x => x.Amount.Cost * x.Amount.Remaining), 2)));
        var a = actuals.GroupBy(x => x.Label).ToDictionary(g => g.Key, g => (Hours: g.Sum(x => x.Amount.Hours), Cost: g.Sum(x => x.Amount.Cost)));
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
