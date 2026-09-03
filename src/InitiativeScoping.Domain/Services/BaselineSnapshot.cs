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
            Lines = forecast.Lines.Select(l => new ForecastBaselineLine
            {
                PhaseId = l.Allocation.PhaseId,
                ResourceTypeId = l.Allocation.ResourceTypeId,
                Seniority = l.Allocation.Seniority,
                Location = l.Allocation.Location,
                ResourcingClass = l.Allocation.ResourcingClass,
                Hours = l.Hours,
                HourlyRate = l.HourlyRate!.Value,
                Cost = l.Cost
            }).ToList()
        };

        initiative.Baselines.Add(baseline);
        return baseline;
    }
}
