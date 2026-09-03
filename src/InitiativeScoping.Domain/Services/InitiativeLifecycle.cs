using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>Status transition rules and the guard that must pass before a forecast can be baselined.</summary>
public static class InitiativeLifecycle
{
    private static readonly Dictionary<InitiativeStatus, InitiativeStatus[]> Transitions = new()
    {
        [InitiativeStatus.Draft] = [InitiativeStatus.Active, InitiativeStatus.Cancelled],
        [InitiativeStatus.Active] = [InitiativeStatus.OnHold, InitiativeStatus.Complete, InitiativeStatus.Cancelled],
        [InitiativeStatus.OnHold] = [InitiativeStatus.Active, InitiativeStatus.Cancelled],
        [InitiativeStatus.Complete] = [],
        [InitiativeStatus.Cancelled] = []
    };

    public static bool CanTransition(InitiativeStatus from, InitiativeStatus to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static IReadOnlyList<InitiativeStatus> AllowedTransitions(InitiativeStatus from) =>
        Transitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>Returns the reasons a forecast cannot be baselined; empty when it can.</summary>
    public static IReadOnlyList<string> BaselineBlockers(Initiative initiative, ForecastResult forecast)
    {
        var errors = new List<string>();
        if (initiative.Phases.Count == 0)
        {
            errors.Add("At least one phase is required.");
        }

        if (initiative.Allocations.Count == 0)
        {
            errors.Add("At least one allocation is required.");
        }

        var unpriced = forecast.Lines.Count(l => l.IsUnpriced);
        if (unpriced > 0)
        {
            errors.Add($"{unpriced} allocation line(s) have no matching published rate.");
        }

        if (initiative.Phases.Any(p => p.PlannedEnd < p.PlannedStart))
        {
            errors.Add("Every phase must end on or after it starts.");
        }

        return errors;
    }
}
