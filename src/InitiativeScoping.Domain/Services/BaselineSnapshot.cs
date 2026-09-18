using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

/// <summary>Freezes the current forecast into a new <see cref="ForecastBaseline"/> version and makes it current.</summary>
public static class BaselineSnapshot
{
    public static ForecastBaseline Create(
        Initiative initiative,
        ForecastResult forecast,
        string snapshotBy,
        DateTimeOffset snapshotAt,
        string? reason)
    {
        if (!forecast.IsComplete)
        {
            throw new InvalidOperationException("Cannot baseline a forecast with unpriced lines.");
        }

        foreach (var existing in initiative.Baselines)
        {
            existing.IsCurrent = false;
        }

        var phaseNames = initiative.Phases.ToDictionary(p => p.Id, p => p.Name);

        var baseline = new ForecastBaseline
        {
            InitiativeId = initiative.Id,
            Version = initiative.Baselines.Count == 0 ? 1 : initiative.Baselines.Max(b => b.Version) + 1,
            SnapshotAt = snapshotAt,
            SnapshotBy = snapshotBy,
            Reason = reason,
            IsCurrent = true,
            TotalHours = forecast.TotalHours,
            TotalCost = forecast.TotalCost,
            ContingencyPct = forecast.ContingencyPct,
            ContingencyCost = forecast.ContingencyCost,
            EstimateConfidence = initiative.EstimateConfidence,
            Lines = forecast.Lines.SelectMany(l => SeatLines(l, phaseNames)).ToList(),
            NonLaborLines = forecast.NonLaborLines.Where(l => l.HasWindow).Select(l => new ForecastBaselineNonLaborLine
            {
                PhaseId = l.Line.PhaseId,
                PhaseName = l.Line.PhaseId is { } pid ? phaseNames.GetValueOrDefault(pid, $"Phase #{pid}") : null,
                Category = l.Line.Category,
                Description = l.Line.Description,
                BillingModel = l.Line.BillingModel,
                Quantity = l.Line.Quantity,
                UnitCost = l.Line.UnitCost,
                Periods = l.Periods,
                StartDate = l.Start!.Value,
                EndDate = l.End!.Value,
                CapexPercent = l.Line.CapexPercent,
                Cost = l.Cost
            }).ToList()
        };

        initiative.Baselines.Add(baseline);
        return baseline;
    }

    /// <summary>
    /// One baseline line per named person (one seat each) plus one line for the remaining unnamed seats, so the
    /// snapshot records who was staffed. Hours and cost are split evenly per seat; the last line takes any rounding remainder.
    /// </summary>
    private static IEnumerable<ForecastBaselineLine> SeatLines(ForecastLine l, IReadOnlyDictionary<int, string> phaseNames)
    {
        var a = l.Allocation;
        if (a.People.Count == 0)
        {
            yield return Line(l, phaseNames, null, null, l.Hours, l.Cost);
            yield break;
        }

        var seats = Math.Max(a.Quantity, a.People.Count);
        var seatHours = Math.Round(l.Hours / seats, 2, MidpointRounding.AwayFromZero);
        var seatCost = Math.Round(l.Cost / seats, 2, MidpointRounding.AwayFromZero);
        var unnamed = seats - a.People.Count;
        var (hoursLeft, costLeft) = (l.Hours, l.Cost);
        for (var i = 0; i < a.People.Count; i++)
        {
            var p = a.People[i];
            var last = unnamed == 0 && i == a.People.Count - 1;
            var hours = last ? hoursLeft : seatHours;
            var cost = last ? costLeft : seatCost;
            hoursLeft -= hours;
            costLeft -= cost;
            yield return Line(l, phaseNames, p.PersonId, p.Person?.DisplayName ?? $"Person #{p.PersonId}", hours, cost);
        }

        if (unnamed > 0)
        {
            yield return Line(l, phaseNames, null, null, hoursLeft, costLeft);
        }
    }

    private static ForecastBaselineLine Line(ForecastLine l, IReadOnlyDictionary<int, string> phaseNames, int? personId, string? personName, decimal hours, decimal cost) =>
        new()
        {
            PhaseId = l.Allocation.PhaseId,
            BusinessUnitId = l.Allocation.BusinessUnitId,
            ResourceTypeId = l.Allocation.ResourceTypeId,
            SeniorityId = l.Allocation.SeniorityId,
            Location = l.Allocation.Location,
            ResourcingClassId = l.Allocation.ResourcingClassId,
            ResourcingClassName = l.Allocation.ResourcingClass?.Name ?? $"Class #{l.Allocation.ResourcingClassId}",
            VendorId = l.Allocation.VendorId,
            PhaseName = l.Allocation.Phase?.Name ?? phaseNames.GetValueOrDefault(l.Allocation.PhaseId, $"Phase #{l.Allocation.PhaseId}"),
            BusinessUnitName = l.Allocation.BusinessUnit?.Name ?? $"BU #{l.Allocation.BusinessUnitId}",
            ResourceTypeName = l.Allocation.ResourceType?.Name ?? $"Type #{l.Allocation.ResourceTypeId}",
            SeniorityName = l.Allocation.Seniority?.Name ?? $"Seniority #{l.Allocation.SeniorityId}",
            VendorName = l.Allocation.Vendor?.Name ?? (l.Allocation.VendorId is { } vid ? $"Vendor #{vid}" : null),
            PersonId = personId,
            PersonName = personName,
            CapexPercent = l.Allocation.CapexPercent,
            Hours = hours,
            HourlyRate = l.HourlyRate!.Value,
            Cost = cost
        };
}
