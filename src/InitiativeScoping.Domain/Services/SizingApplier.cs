using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

public sealed record SizedAllocation(string PhaseName, int ResourceTypeId, Seniority Seniority, decimal Hours);

public static class SizingApplier
{
    /// <summary>
    /// Splits <paramref name="totalHours"/> across template lines by percentage.
    /// The last line absorbs rounding so the result sums exactly to the total.
    /// </summary>
    public static IReadOnlyList<SizedAllocation> Apply(decimal totalHours, IReadOnlyList<AllocationTemplateLine> lines)
    {
        if (lines.Count == 0)
        {
            return [];
        }

        var result = new List<SizedAllocation>(lines.Count);
        decimal allocated = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var hours = i == lines.Count - 1
                ? totalHours - allocated
                : Math.Round(totalHours * line.Percent / 100m, 2, MidpointRounding.AwayFromZero);
            allocated += hours;
            result.Add(new SizedAllocation(line.PhaseName, line.ResourceTypeId, line.Seniority, hours));
        }

        return result;
    }

    /// <summary>Distinct phase names in template order.</summary>
    public static IReadOnlyList<string> PhaseNames(IEnumerable<AllocationTemplateLine> lines) =>
        lines.Select(l => l.PhaseName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
