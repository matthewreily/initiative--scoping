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

public sealed record ForecastResult(IReadOnlyList<ForecastLine> Lines)
{
    public decimal TotalHours => Lines.Sum(l => l.Hours);
    public decimal TotalCost => Lines.Sum(l => l.Cost);
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

        return new ForecastResult(lines);
    }
}
