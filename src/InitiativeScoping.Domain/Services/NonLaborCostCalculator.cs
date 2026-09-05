using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>Whole-period billing for non-labor cost lines: any partial month/year in the window counts as a full one.</summary>
public static class NonLaborCostCalculator
{
    /// <summary>
    /// Number of billable periods between <paramref name="start"/> and <paramref name="end"/> inclusive.
    /// Periods are counted from the start date's anniversary (15 Jan–14 Feb is one month, 15 Jan–15 Feb is two;
    /// 31 Jan–28 Feb is one because the day of month never reaches 31). Returns 0 when end precedes start.
    /// </summary>
    public static int BillablePeriods(BillingModel model, DateOnly start, DateOnly end)
    {
        if (end < start)
        {
            return 0;
        }

        switch (model)
        {
            case BillingModel.OneTime:
                return 1;
            case BillingModel.Monthly:
                var months = (end.Year - start.Year) * 12 + end.Month - start.Month;
                return months + (end.Day >= start.Day ? 1 : 0);
            default:
                var years = end.Year - start.Year;
                var pastAnniversary = end.Month > start.Month || (end.Month == start.Month && end.Day >= start.Day);
                return years + (pastAnniversary ? 1 : 0);
        }
    }

    public static decimal Cost(BillingModel model, decimal unitCost, int quantity, DateOnly start, DateOnly end) =>
        Math.Round(unitCost * quantity * BillablePeriods(model, start, end), 2, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The window a line is billed over: explicit dates when set, else the phase's planned window,
    /// else the initiative's target window (falling back to the last phase end, then the target start, when there is no target end).
    /// Null when the line references a phase that no longer exists.
    /// </summary>
    public static (DateOnly Start, DateOnly End)? Window(InitiativeNonLaborCost line, Initiative initiative)
    {
        DateOnly start, end;
        if (line.PhaseId is { } phaseId)
        {
            var phase = line.Phase ?? initiative.Phases.FirstOrDefault(p => p.Id == phaseId);
            if (phase is null)
            {
                return null;
            }

            (start, end) = (phase.PlannedStart, phase.PlannedEnd);
        }
        else
        {
            (start, end) = (initiative.TargetStart, InitiativeEnd(initiative));
        }

        return (line.StartDate ?? start, line.EndDate ?? end);
    }

    /// <summary>Target end, else the last phase end, else the target start; never before the target start.</summary>
    public static DateOnly InitiativeEnd(Initiative initiative)
    {
        var end = initiative.TargetEnd
            ?? (initiative.Phases.Count > 0 ? initiative.Phases.Max(p => p.PlannedEnd) : initiative.TargetStart);
        return end < initiative.TargetStart ? initiative.TargetStart : end;
    }
}
