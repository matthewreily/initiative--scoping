using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>One calendar month of an initiative's cost profile. <see cref="Month"/> is the first day of the month.</summary>
public sealed record MonthBucket(
    DateOnly Month,
    decimal ForecastLaborCost,
    decimal ForecastNonLaborCost,
    decimal ForecastHours,
    decimal BaselineCost,
    decimal ActualCost,
    decimal CumulativeForecastCost,
    decimal CumulativeBaselineCost,
    decimal CumulativeActualCost)
{
    public decimal ForecastCost => ForecastLaborCost + ForecastNonLaborCost;
    public decimal VarianceToBaseline => ForecastCost - BaselineCost;
}

public sealed record MonthlyPhasing(IReadOnlyList<MonthBucket> Months)
{
    public static readonly MonthlyPhasing Empty = new([]);

    public bool IsEmpty => Months.Count == 0;
    public decimal ForecastCost => Months.Sum(m => m.ForecastCost);
    public decimal BaselineCost => Months.Sum(m => m.BaselineCost);
    public decimal ActualCost => Months.Sum(m => m.ActualCost);
    public decimal PeakMonthCost => Months.Count == 0 ? 0m : Months.Max(m => Math.Max(m.ForecastCost, Math.Max(m.BaselineCost, m.ActualCost)));

    /// <summary>Calendar-year subtotals in chronological order.</summary>
    public IReadOnlyList<(int Year, decimal ForecastCost, decimal BaselineCost, decimal ActualCost)> ByYear =>
        Months.GroupBy(m => m.Month.Year)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(m => m.ForecastCost), g.Sum(m => m.BaselineCost), g.Sum(m => m.ActualCost)))
            .ToList();

    /// <summary>Sums several initiatives' profiles month by month.</summary>
    public static MonthlyPhasing Combine(IEnumerable<MonthlyPhasing> profiles)
    {
        var buckets = new SortedDictionary<DateOnly, (decimal Labor, decimal NonLabor, decimal Hours, decimal Baseline, decimal Actual)>();
        foreach (var m in profiles.SelectMany(p => p.Months))
        {
            var b = buckets.GetValueOrDefault(m.Month);
            buckets[m.Month] = (b.Labor + m.ForecastLaborCost, b.NonLabor + m.ForecastNonLaborCost, b.Hours + m.ForecastHours, b.Baseline + m.BaselineCost, b.Actual + m.ActualCost);
        }

        return MonthlyPhasingCalculator.Build(buckets);
    }
}

/// <summary>
/// Spreads forecast, baseline and actual cost across calendar months so a plan can be read as a cash-flow profile.
/// Labor lines are spread over their phase window in proportion to calendar days per month; recurring non-labor
/// lines land one unit price in the month each billable period starts; one-time lines land in their start month.
/// Actual entries fall in the month of their work date, manual adjustments in the month they were recorded.
/// Baseline labor lines use the phase's <em>current</em> dates (the snapshot stores hours and cost, not dates).
/// Every month between the earliest and latest touched month is present, including empty ones.
/// </summary>
public static class MonthlyPhasingCalculator
{
    public static MonthlyPhasing Calculate(
        Initiative initiative,
        ForecastResult forecast,
        ForecastBaseline? baseline,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments)
    {
        var phases = initiative.Phases.ToDictionary(p => p.Id);
        var buckets = new SortedDictionary<DateOnly, (decimal Labor, decimal NonLabor, decimal Hours, decimal Baseline, decimal Actual)>();

        void Add(DateOnly month, decimal labor = 0m, decimal nonLabor = 0m, decimal hours = 0m, decimal baselineCost = 0m, decimal actual = 0m)
        {
            var b = buckets.GetValueOrDefault(month);
            buckets[month] = (b.Labor + labor, b.NonLabor + nonLabor, b.Hours + hours, b.Baseline + baselineCost, b.Actual + actual);
        }

        foreach (var line in forecast.Lines)
        {
            if (!phases.TryGetValue(line.Allocation.PhaseId, out var phase))
            {
                continue;
            }

            var costShares = SpreadByDays(line.Cost, phase.PlannedStart, phase.PlannedEnd);
            var hourShares = SpreadByDays(line.Hours, phase.PlannedStart, phase.PlannedEnd);
            for (var k = 0; k < costShares.Count; k++)
            {
                Add(costShares[k].Month, labor: costShares[k].Amount, hours: hourShares[k].Amount);
            }
        }

        foreach (var line in forecast.NonLaborLines.Where(l => l.HasWindow && l.Periods > 0))
        {
            foreach (var (month, amount) in SpreadByPeriods(line.Cost, line.Periods, line.Line.BillingModel, line.Start!.Value))
            {
                Add(month, nonLabor: amount);
            }
        }

        if (baseline is not null)
        {
            foreach (var line in baseline.Lines)
            {
                if (!phases.TryGetValue(line.PhaseId, out var phase))
                {
                    continue;
                }

                foreach (var (month, amount) in SpreadByDays(line.Cost, phase.PlannedStart, phase.PlannedEnd))
                {
                    Add(month, baselineCost: amount);
                }
            }

            foreach (var line in baseline.NonLaborLines.Where(l => l.Periods > 0))
            {
                foreach (var (month, amount) in SpreadByPeriods(line.Cost, line.Periods, line.BillingModel, line.StartDate))
                {
                    Add(month, baselineCost: amount);
                }
            }
        }

        foreach (var e in entries.Where(e => !e.IsUnmapped && e.InitiativeId == initiative.Id))
        {
            Add(FirstOfMonth(e.WorkDate), actual: e.EffectiveCost ?? 0m);
        }

        foreach (var a in adjustments)
        {
            Add(FirstOfMonth(DateOnly.FromDateTime(a.CreatedAt.UtcDateTime)), actual: a.Cost);
        }

        return Build(buckets);
    }

    internal static MonthlyPhasing Build(SortedDictionary<DateOnly, (decimal Labor, decimal NonLabor, decimal Hours, decimal Baseline, decimal Actual)> buckets)
    {
        if (buckets.Count == 0)
        {
            return MonthlyPhasing.Empty;
        }

        var months = new List<MonthBucket>();
        decimal cumForecast = 0m, cumBaseline = 0m, cumActual = 0m;
        for (var m = buckets.Keys.First(); m <= buckets.Keys.Last(); m = m.AddMonths(1))
        {
            var b = buckets.GetValueOrDefault(m);
            cumForecast += b.Labor + b.NonLabor;
            cumBaseline += b.Baseline;
            cumActual += b.Actual;
            months.Add(new MonthBucket(m, b.Labor, b.NonLabor, b.Hours, b.Baseline, b.Actual, cumForecast, cumBaseline, cumActual));
        }

        return new MonthlyPhasing(months);
    }

    public static DateOnly FirstOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    /// <summary>
    /// Splits <paramref name="amount"/> across the months touched by [start, end] in proportion to calendar days,
    /// rounded to 2 dp with the last month absorbing the rounding remainder so the shares always sum to the amount.
    /// </summary>
    public static IReadOnlyList<(DateOnly Month, decimal Amount)> SpreadByDays(decimal amount, DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            (start, end) = (end, start);
        }

        var totalDays = end.DayNumber - start.DayNumber + 1;
        var result = new List<(DateOnly, decimal)>();
        var allocated = 0m;
        for (var month = FirstOfMonth(start); month <= end; month = month.AddMonths(1))
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            var from = start > month ? start : month;
            var to = end < monthEnd ? end : monthEnd;
            var days = to.DayNumber - from.DayNumber + 1;
            var isLast = monthEnd >= end;
            var share = isLast ? amount - allocated : Math.Round(amount * days / totalDays, 2, MidpointRounding.AwayFromZero);
            allocated += share;
            result.Add((month, share));
        }

        return result;
    }

    /// <summary>One equal share per billable period, dated at each period's start (monthly/yearly anniversaries of <paramref name="start"/>).</summary>
    public static IReadOnlyList<(DateOnly Month, decimal Amount)> SpreadByPeriods(decimal amount, int periods, BillingModel model, DateOnly start)
    {
        if (periods <= 0)
        {
            return [];
        }

        var perPeriod = Math.Round(amount / periods, 2, MidpointRounding.AwayFromZero);
        var result = new List<(DateOnly, decimal)>(periods);
        var allocated = 0m;
        for (var k = 0; k < periods; k++)
        {
            var periodStart = model switch
            {
                BillingModel.Monthly => start.AddMonths(k),
                BillingModel.Annual => start.AddYears(k),
                _ => start
            };
            var share = k == periods - 1 ? amount - allocated : perPeriod;
            allocated += share;
            result.Add((FirstOfMonth(periodStart), share));
        }

        return result;
    }
}
