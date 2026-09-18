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
    decimal CumulativeActualCost,
    decimal ForecastCapexCost = 0m,
    decimal BaselineCapexCost = 0m)
{
    public decimal ForecastCost => ForecastLaborCost + ForecastNonLaborCost;
    public decimal ForecastOpexCost => ForecastCost - ForecastCapexCost;
    public decimal BaselineOpexCost => BaselineCost - BaselineCapexCost;
    public decimal VarianceToBaseline => ForecastCost - BaselineCost;
}

/// <summary>Fiscal calendar: the month a fiscal year starts in. Fiscal years are named after the calendar year they end in.</summary>
public sealed record FiscalCalendar(int StartMonth)
{
    public static readonly FiscalCalendar Calendar = new(1);

    public int FiscalYearOf(DateOnly date) => StartMonth == 1 || date.Month < StartMonth ? date.Year : date.Year + 1;

    public int QuarterOf(DateOnly date) => ((date.Month - StartMonth + 12) % 12) / 3 + 1;

    public DateOnly StartOf(int fiscalYear) => new(StartMonth == 1 ? fiscalYear : fiscalYear - 1, StartMonth, 1);

    public string Label(int fiscalYear) => StartMonth == 1 ? fiscalYear.ToString() : $"FY{fiscalYear}";
}

/// <summary>A fiscal year or quarter subtotal of a monthly profile. <see cref="Quarter"/> is null for year rows.</summary>
public sealed record FiscalPeriodBucket(
    int FiscalYear,
    int? Quarter,
    string Label,
    DateOnly Start,
    DateOnly End,
    decimal ForecastHours,
    decimal ForecastLaborCost,
    decimal ForecastNonLaborCost,
    decimal ForecastCapexCost,
    decimal BaselineCost,
    decimal BaselineCapexCost,
    decimal ActualCost)
{
    public decimal ForecastCost => ForecastLaborCost + ForecastNonLaborCost;
    public decimal ForecastOpexCost => ForecastCost - ForecastCapexCost;
    public decimal BaselineOpexCost => BaselineCost - BaselineCapexCost;
    public decimal VarianceToBaseline => ForecastCost - BaselineCost;
}

public sealed record MonthlyPhasing(IReadOnlyList<MonthBucket> Months)
{
    public static readonly MonthlyPhasing Empty = new([]);

    public bool IsEmpty => Months.Count == 0;
    public decimal ForecastCost => Months.Sum(m => m.ForecastCost);
    public decimal BaselineCost => Months.Sum(m => m.BaselineCost);
    public decimal ActualCost => Months.Sum(m => m.ActualCost);
    public decimal ForecastCapexCost => Months.Sum(m => m.ForecastCapexCost);
    public decimal ForecastOpexCost => ForecastCost - ForecastCapexCost;
    public decimal BaselineCapexCost => Months.Sum(m => m.BaselineCapexCost);
    public decimal PeakMonthCost => Months.Count == 0 ? 0m : Months.Max(m => Math.Max(m.ForecastCost, Math.Max(m.BaselineCost, m.ActualCost)));

    /// <summary>Calendar-year subtotals in chronological order.</summary>
    public IReadOnlyList<(int Year, decimal ForecastCost, decimal BaselineCost, decimal ActualCost)> ByYear =>
        Months.GroupBy(m => m.Month.Year)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Sum(m => m.ForecastCost), g.Sum(m => m.BaselineCost), g.Sum(m => m.ActualCost)))
            .ToList();

    /// <summary>
    /// Fiscal-year rows, each followed by its quarter rows, in chronological order. Only quarters that touch the profile are
    /// listed; a fiscal year row covers the whole year window regardless.
    /// </summary>
    public IReadOnlyList<FiscalPeriodBucket> ByFiscalPeriod(FiscalCalendar calendar)
    {
        var result = new List<FiscalPeriodBucket>();
        foreach (var year in Months.GroupBy(m => calendar.FiscalYearOf(m.Month)).OrderBy(g => g.Key))
        {
            var start = calendar.StartOf(year.Key);
            result.Add(Sum(year, year.Key, null, calendar.Label(year.Key), start, start.AddYears(1).AddDays(-1)));
            foreach (var quarter in year.GroupBy(m => calendar.QuarterOf(m.Month)).OrderBy(g => g.Key))
            {
                var qStart = start.AddMonths((quarter.Key - 1) * 3);
                result.Add(Sum(quarter, year.Key, quarter.Key, $"{calendar.Label(year.Key)} Q{quarter.Key}", qStart, qStart.AddMonths(3).AddDays(-1)));
            }
        }

        return result;

        static FiscalPeriodBucket Sum(IEnumerable<MonthBucket> months, int fy, int? quarter, string label, DateOnly start, DateOnly end)
        {
            var list = months.ToList();
            return new FiscalPeriodBucket(fy, quarter, label, start, end,
                list.Sum(m => m.ForecastHours), list.Sum(m => m.ForecastLaborCost), list.Sum(m => m.ForecastNonLaborCost), list.Sum(m => m.ForecastCapexCost),
                list.Sum(m => m.BaselineCost), list.Sum(m => m.BaselineCapexCost), list.Sum(m => m.ActualCost));
        }
    }

    /// <summary>Sums several initiatives' profiles month by month.</summary>
    public static MonthlyPhasing Combine(IEnumerable<MonthlyPhasing> profiles)
    {
        var buckets = new SortedDictionary<DateOnly, MonthlyPhasingCalculator.Bucket>();
        foreach (var m in profiles.SelectMany(p => p.Months))
        {
            buckets[m.Month] = buckets.GetValueOrDefault(m.Month).Plus(m.ForecastLaborCost, m.ForecastNonLaborCost, m.ForecastHours, m.BaselineCost, m.ActualCost, m.ForecastCapexCost, m.BaselineCapexCost);
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
    internal readonly record struct Bucket(decimal Labor, decimal NonLabor, decimal Hours, decimal Baseline, decimal Actual, decimal ForecastCapex, decimal BaselineCapex)
    {
        public Bucket Plus(decimal labor = 0m, decimal nonLabor = 0m, decimal hours = 0m, decimal baseline = 0m, decimal actual = 0m, decimal forecastCapex = 0m, decimal baselineCapex = 0m) =>
            new(Labor + labor, NonLabor + nonLabor, Hours + hours, Baseline + baseline, Actual + actual, ForecastCapex + forecastCapex, BaselineCapex + baselineCapex);
    }

    public static MonthlyPhasing Calculate(
        Initiative initiative,
        ForecastResult forecast,
        ForecastBaseline? baseline,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments)
    {
        var phases = initiative.Phases.ToDictionary(p => p.Id);
        var buckets = new SortedDictionary<DateOnly, Bucket>();

        void Add(DateOnly month, decimal labor = 0m, decimal nonLabor = 0m, decimal hours = 0m, decimal baselineCost = 0m, decimal actual = 0m, decimal forecastCapex = 0m, decimal baselineCapex = 0m) =>
            buckets[month] = buckets.GetValueOrDefault(month).Plus(labor, nonLabor, hours, baselineCost, actual, forecastCapex, baselineCapex);

        static decimal CapexPart(CapitalizationType c, decimal amount) => c == CapitalizationType.Capex ? amount : 0m;

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
                Add(costShares[k].Month, labor: costShares[k].Amount, hours: hourShares[k].Amount, forecastCapex: CapexPart(line.Allocation.Capitalization, costShares[k].Amount));
            }
        }

        foreach (var line in forecast.NonLaborLines.Where(l => l.HasWindow && l.Periods > 0))
        {
            foreach (var (month, amount) in SpreadByPeriods(line.Cost, line.Periods, line.Line.BillingModel, line.Start!.Value))
            {
                Add(month, nonLabor: amount, forecastCapex: CapexPart(line.Line.Capitalization, amount));
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
                    Add(month, baselineCost: amount, baselineCapex: CapexPart(line.Capitalization, amount));
                }
            }

            foreach (var line in baseline.NonLaborLines.Where(l => l.Periods > 0))
            {
                foreach (var (month, amount) in SpreadByPeriods(line.Cost, line.Periods, line.BillingModel, line.StartDate))
                {
                    Add(month, baselineCost: amount, baselineCapex: CapexPart(line.Capitalization, amount));
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

    internal static MonthlyPhasing Build(SortedDictionary<DateOnly, Bucket> buckets)
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
            months.Add(new MonthBucket(m, b.Labor, b.NonLabor, b.Hours, b.Baseline, b.Actual, cumForecast, cumBaseline, cumActual, b.ForecastCapex, b.BaselineCapex));
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
