using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record ForecastLine(
    InitiativeAllocation Allocation,
    decimal Hours,
    decimal? HourlyRate)
{
    public bool IsUnpriced => HourlyRate is null;
    public decimal Cost => Hours * (HourlyRate ?? 0m);
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

public sealed record ForecastResult(IReadOnlyList<ForecastLine> Lines, IReadOnlyList<NonLaborForecastLine> NonLaborLines)
{
    public ForecastResult(IReadOnlyList<ForecastLine> lines) : this(lines, []) { }

    public decimal TotalHours => Lines.Sum(l => l.Hours);
    public decimal LaborCost => Lines.Sum(l => l.Cost);
    public decimal NonLaborCost => NonLaborLines.Sum(l => l.Cost);
    public decimal TotalCost => LaborCost + NonLaborCost;
    public bool IsComplete => Lines.All(l => !l.IsUnpriced);
}

public static class ForecastCalculator
{
    /// <summary>
    /// Hours = Quantity x EstimatedHours; rate resolved against the rate card in effect at the phase planned start.
    /// </summary>
    public static ForecastResult Calculate(Initiative initiative, IReadOnlyCollection<RateCard> rateCards)
    {
        var phases = initiative.Phases.ToDictionary(p => p.Id);
        var lines = initiative.Allocations.Select(a =>
        {
            var asOf = phases.TryGetValue(a.PhaseId, out var phase) ? phase.PlannedStart : initiative.TargetStart;
            var rate = RateResolver.Resolve(rateCards,
                new RateKey(a.ResourceTypeId, initiative.BusinessUnitId, a.Seniority, a.Location, a.ResourcingClass),
                asOf);
            return new ForecastLine(a, a.Quantity * a.EstimatedHours, rate);
        }).ToList();

        var nonLabor = initiative.NonLaborCosts.Select(c => PriceNonLabor(c, initiative)).ToList();

        return new ForecastResult(lines, nonLabor);
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
