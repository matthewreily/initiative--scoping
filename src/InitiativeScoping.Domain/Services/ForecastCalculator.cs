using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record ForecastLine(
    InitiativeAllocation Allocation,
    decimal Hours,
    decimal? HourlyRate,
    IReadOnlyList<RateSegment>? RateSegments = null)
{
    public bool IsUnpriced => HourlyRate is null;
    /// <summary>True when the phase spans more than one rate card, so <see cref="HourlyRate"/> is a day-weighted blend.</summary>
    public bool IsBlendedRate => RateSegments is { Count: > 1 };
    public decimal Cost => Hours * (HourlyRate ?? 0m);
    /// <summary>Share of one seat when the allocation's quantity is spread evenly over its named and unnamed seats.</summary>
    public decimal HoursPerSeat => Hours / Math.Max(1, Math.Max(Allocation.Quantity, Allocation.People.Count));
    public decimal CostPerSeat => Cost / Math.Max(1, Math.Max(Allocation.Quantity, Allocation.People.Count));
}

/// <summary>Priced non-labor line. <see cref="Periods"/> is 0 (and cost 0) when the window is empty or its phase is missing.</summary>
public sealed record NonLaborForecastLine(
    InitiativeNonLaborCost Line,
    DateOnly? Start,
    DateOnly? End,
    int Periods,
    decimal Cost)
{
    public bool HasWindow => Start is not null;
}

public sealed record ForecastResult(IReadOnlyList<ForecastLine> Lines, IReadOnlyList<NonLaborForecastLine> NonLaborLines, decimal ContingencyPct = 0m)
{
    public ForecastResult(IReadOnlyList<ForecastLine> lines) : this(lines, []) { }

    public decimal TotalHours => Lines.Sum(l => l.Hours);
    public decimal LaborCost => Lines.Sum(l => l.Cost);
    public decimal NonLaborCost => NonLaborLines.Sum(l => l.Cost);
    /// <summary>Priced labor + non-labor, before contingency.</summary>
    public decimal TotalCost => LaborCost + NonLaborCost;
    public decimal ContingencyCost => ContingencyCalculator.Reserve(TotalCost, ContingencyPct);
    public decimal TotalCostWithContingency => TotalCost + ContingencyCost;
    public bool IsComplete => Lines.All(l => !l.IsUnpriced);
}

public static class ForecastCalculator
{
    /// <summary>
    /// Hours = Quantity x EstimatedHours; rate is the day-weighted blend of the rate cards in effect across the phase's
    /// planned window (a single card for most phases), keyed by resource type, seniority, location, class and vendor.
    /// </summary>
    public static ForecastResult Calculate(Initiative initiative, IReadOnlyCollection<RateCard> rateCards)
    {
        var phases = initiative.Phases.Where(p => p.Id != 0).ToDictionary(p => p.Id);
        var lines = initiative.Allocations.Select(a =>
        {
            var phase = a.Phase ?? phases.GetValueOrDefault(a.PhaseId);
            var start = phase?.PlannedStart ?? initiative.TargetStart;
            var end = phase?.PlannedEnd ?? start;
            var key = new RateKey(a.ResourceTypeId, a.SeniorityId, a.Location, a.ResourcingClass, a.VendorId);
            var segments = RateResolver.Segments(rateCards, key, start, end);
            return new ForecastLine(a, a.Quantity * a.EstimatedHours, RateResolver.Blend(segments), segments);
        }).ToList();

        var nonLabor = initiative.NonLaborCosts.Select(c => PriceNonLabor(c, initiative)).ToList();

        return new ForecastResult(lines, nonLabor, initiative.ContingencyPct);
    }

    public static NonLaborForecastLine PriceNonLabor(InitiativeNonLaborCost line, Initiative initiative)
    {
        var window = NonLaborCostCalculator.Window(line, initiative);
        if (window is null)
        {
            return new NonLaborForecastLine(line, null, null, 0, 0m);
        }

        var (start, end) = window.Value;
        var periods = NonLaborCostCalculator.BillablePeriods(line.BillingModel, start, end);
        return new NonLaborForecastLine(line, start, end, periods,
            NonLaborCostCalculator.Cost(line.BillingModel, line.UnitCost, line.Quantity, start, end));
    }
}
