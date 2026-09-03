using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>One initiative's line on the portfolio dashboard: live forecast, current baseline and spent-to-date side by side.</summary>
public sealed record PortfolioRow(
    Initiative Initiative,
    ForecastResult Forecast,
    VarianceResult Variance)
{
    public decimal ForecastHours => Forecast.TotalHours;
    public decimal ForecastCost => Forecast.TotalCost;
    public decimal InternalForecastCost => Forecast.Lines.Where(l => l.Allocation.ResourcingClass == ResourcingClass.InternalFte).Sum(l => l.Cost);
    public decimal VendorForecastCost => Forecast.Lines.Where(l => l.Allocation.ResourcingClass == ResourcingClass.Vendor).Sum(l => l.Cost);
    public bool HasBaseline => Variance.Baseline is not null;
    public int? BaselineVersion => Variance.Baseline?.Version;
    public decimal BaselineHours => Variance.BaselineHours;
    public decimal BaselineCost => Variance.BaselineCost;
    public decimal ActualHours => Variance.ActualHours;
    public decimal ActualCost => Variance.ActualCost;
    public decimal CostVariance => Variance.CostVariance;
    public decimal? CostVariancePct => Variance.CostVariancePct;
    public bool ExceedsThreshold => Variance.ExceedsThreshold;
    public bool HasUnpricedForecast => !Forecast.IsComplete;
    public bool HasUnpricedActuals => Variance.UnpricedEntries > 0;

    /// <summary>Spent as a share of baseline (or of the live forecast for un-baselined initiatives), capped at 100 for rendering.</summary>
    public decimal BurnPct
    {
        get
        {
            var denominator = HasBaseline ? BaselineCost : ForecastCost;
            return denominator <= 0 ? 0 : Math.Min(100m, Math.Round(ActualCost / denominator * 100m, 1));
        }
    }
}

public sealed record PortfolioGroup(string Label, int Count, decimal ForecastCost, decimal BaselineCost, decimal ActualCost, int OverThreshold)
{
    public decimal CostVariance => ActualCost - BaselineCost;
    public decimal? CostVariancePct => BaselineCost == 0 ? null : Math.Round(CostVariance / BaselineCost * 100m, 1);
}

public sealed record PortfolioResult(IReadOnlyList<PortfolioRow> Rows)
{
    public int Count => Rows.Count;
    public decimal ForecastHours => Rows.Sum(r => r.ForecastHours);
    public decimal ForecastCost => Rows.Sum(r => r.ForecastCost);
    public decimal InternalForecastCost => Rows.Sum(r => r.InternalForecastCost);
    public decimal VendorForecastCost => Rows.Sum(r => r.VendorForecastCost);
    public decimal BaselineHours => Rows.Sum(r => r.BaselineHours);
    public decimal BaselineCost => Rows.Sum(r => r.BaselineCost);
    public decimal ActualHours => Rows.Sum(r => r.ActualHours);
    public decimal ActualCost => Rows.Sum(r => r.ActualCost);
    public decimal CostVariance => ActualCost - BaselineCost;
    public decimal? CostVariancePct => BaselineCost == 0 ? null : Math.Round(CostVariance / BaselineCost * 100m, 1);
    public int Baselined => Rows.Count(r => r.HasBaseline);
    public int OverThreshold => Rows.Count(r => r.ExceedsThreshold);
    public int Unpriced => Rows.Count(r => r.HasUnpricedForecast || r.HasUnpricedActuals);

    public IReadOnlyList<PortfolioGroup> ByBusinessUnit =>
        Group(r => r.Initiative.BusinessUnit?.Name ?? "?").OrderByDescending(g => g.ForecastCost).ToList();

    public IReadOnlyList<PortfolioGroup> ByStatus =>
        Rows.GroupBy(r => r.Initiative.Status).OrderBy(g => g.Key).Select(g => ToGroup(g.Key.ToString(), g)).ToList();

    private IEnumerable<PortfolioGroup> Group(Func<PortfolioRow, string> key) => Rows.GroupBy(key).Select(g => ToGroup(g.Key, g));

    private static PortfolioGroup ToGroup(string label, IEnumerable<PortfolioRow> rows)
    {
        var list = rows.ToList();
        return new PortfolioGroup(label, list.Count,
            list.Sum(r => r.ForecastCost), list.Sum(r => r.BaselineCost), list.Sum(r => r.ActualCost),
            list.Count(r => r.ExceedsThreshold));
    }
}

public static class PortfolioCalculator
{
    /// <summary>
    /// Rolls every initiative up with the same rules as its own pages: live forecast via <see cref="ForecastCalculator"/>,
    /// spent-to-date vs. current baseline via <see cref="VarianceCalculator"/>. Unmapped entries are excluded; unpriced ones count as $0 and are flagged.
    /// </summary>
    public static PortfolioResult Calculate(
        IReadOnlyList<Initiative> initiatives,
        IReadOnlyCollection<RateCard> rateCards,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments,
        IReadOnlyDictionary<int, string> resourceTypeNames,
        decimal? defaultThresholdPct)
    {
        var entriesByInitiative = entries.Where(e => e.InitiativeId is not null).ToLookup(e => e.InitiativeId!.Value);
        var adjustmentsByInitiative = adjustments.ToLookup(a => a.InitiativeId);

        var rows = initiatives.Select(i => new PortfolioRow(
                i,
                ForecastCalculator.Calculate(i, rateCards),
                VarianceCalculator.Calculate(i, entriesByInitiative[i.Id].ToList(), adjustmentsByInitiative[i.Id].ToList(), resourceTypeNames, defaultThresholdPct)))
            .ToList();

        return new PortfolioResult(rows);
    }
}
