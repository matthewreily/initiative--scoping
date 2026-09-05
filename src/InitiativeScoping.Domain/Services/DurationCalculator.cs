using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

/// <summary>Working-day arithmetic for fixed-duration initiatives (Mon–Fri minus holidays).</summary>
public static class DurationCalculator
{
    public static int WorkingDays(DateOnly start, DateOnly end, IReadOnlySet<DateOnly> holidays)
    {
        if (end < start)
        {
            return 0;
        }

        var days = 0;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(d))
            {
                days++;
            }
        }

        return days;
    }

    /// <summary>
    /// Hours per person: percent/100 × working days × hours per day, rounded to 2 dp.
    /// Allocation quantity multiplies this downstream, exactly as for entered hours.
    /// </summary>
    public static decimal Hours(decimal percent, int workingDays, decimal hoursPerDay) =>
        Math.Round(percent / 100m * workingDays * hoursPerDay, 2, MidpointRounding.AwayFromZero);

    public static decimal Hours(InitiativeAllocation allocation, Phase phase, IReadOnlySet<DateOnly> holidays, decimal hoursPerDay) =>
        Hours(allocation.AllocationPercent ?? 0, WorkingDays(phase.PlannedStart, phase.PlannedEnd, holidays), hoursPerDay);

    /// <summary>Percent of one person needed to deliver <paramref name="hours"/> in the window; 0 when the window has no working days.</summary>
    public static decimal PercentFor(decimal hours, int workingDays, decimal hoursPerDay)
    {
        var capacity = workingDays * hoursPerDay;
        return capacity <= 0 ? 0 : Math.Round(hours / capacity * 100m, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Splits [start, end] into consecutive phase windows whose lengths (in calendar days) are proportional to
    /// <paramref name="weights"/>. Every phase gets at least one day; the last absorbs rounding.
    /// </summary>
    public static IReadOnlyList<(DateOnly Start, DateOnly End)> SplitWindow(DateOnly start, DateOnly end, IReadOnlyList<decimal> weights)
    {
        if (weights.Count == 0 || end < start)
        {
            return [];
        }

        var totalDays = end.DayNumber - start.DayNumber + 1;
        var totalWeight = weights.Sum();
        var result = new List<(DateOnly, DateOnly)>(weights.Count);
        var cursor = start;
        for (var i = 0; i < weights.Count; i++)
        {
            var remainingPhases = weights.Count - i;
            var remainingDays = end.DayNumber - cursor.DayNumber + 1;
            int length;
            if (i == weights.Count - 1)
            {
                length = remainingDays;
            }
            else
            {
                var share = totalWeight <= 0 ? 1m / weights.Count : weights[i] / totalWeight;
                length = (int)Math.Round(totalDays * share, MidpointRounding.AwayFromZero);
                length = Math.Clamp(length, 1, Math.Max(1, remainingDays - (remainingPhases - 1)));
            }

            var phaseEnd = cursor.AddDays(length - 1);
            result.Add((cursor, phaseEnd));
            cursor = phaseEnd.AddDays(1);
        }

        return result;
    }

    /// <summary>
    /// Phases must tile [start, end]: ordered by sequence, first starts on <paramref name="start"/>, contiguous (no gaps or
    /// overlaps), never past <paramref name="end"/>. With <paramref name="requireFullCoverage"/> the last phase must also end
    /// exactly on <paramref name="end"/> (checked at activation, so phases can be added one at a time).
    /// </summary>
    public static string? ValidateTiling(DateOnly start, DateOnly end, IEnumerable<Phase> phases, bool requireFullCoverage = false)
    {
        var ordered = phases.OrderBy(p => p.Sequence).ThenBy(p => p.PlannedStart).ToList();
        if (ordered.Count == 0)
        {
            return null;
        }

        var inverted = ordered.FirstOrDefault(p => p.PlannedEnd < p.PlannedStart);
        if (inverted is not null)
        {
            return $"Phase '{inverted.Name}' ends before it starts.";
        }

        if (ordered[0].PlannedStart != start)
        {
            return $"First phase must start on {start:yyyy-MM-dd} (the initiative's target start).";
        }

        for (var i = 1; i < ordered.Count; i++)
        {
            var expected = ordered[i - 1].PlannedEnd.AddDays(1);
            if (ordered[i].PlannedStart != expected)
            {
                return $"Phase '{ordered[i].Name}' must start on {expected:yyyy-MM-dd}, the day after '{ordered[i - 1].Name}' ends.";
            }
        }

        if (ordered[^1].PlannedEnd > end)
        {
            return $"Phase '{ordered[^1].Name}' ends after the initiative's target end {end:yyyy-MM-dd}.";
        }

        if (requireFullCoverage && ordered[^1].PlannedEnd != end)
        {
            return $"Last phase must end on {end:yyyy-MM-dd} (the initiative's target end).";
        }

        return null;
    }
}
