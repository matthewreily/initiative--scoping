namespace InitiativeScoping.Web.Services;

/// <summary>One glossary entry: a metric or concept shown in the UI with a plain-language explanation.</summary>
public sealed record HelpTerm(string Key, string Label, string Text);

/// <summary>Central glossary used by the <c>&lt;help for="…"&gt;</c> tag helper and the Help page, so every
/// tooltip for a given metric says the same thing.</summary>
public static class HelpText
{
    public static readonly IReadOnlyList<HelpTerm> Terms =
    [
        new("forecast-hours", "Forecast hours", "Total planned labor hours across every phase: quantity × hours for each allocation, or staffing % × working days × hours/day in fixed-duration mode."),
        new("forecast-cost", "Forecast cost", "What the current plan is expected to cost: labor (hours × the rate-card rate in effect on each phase day) plus non-labor lines. Excludes contingency."),
        new("with-contingency", "With contingency", "Forecast cost plus the initiative's contingency reserve (contingency % × forecast). This is the figure compared against the approved budget until actuals arrive."),
        new("contingency", "Contingency", "A percentage reserve added on top of the forecast for estimate uncertainty. Captured on each baseline so later changes do not rewrite history."),
        new("confidence", "Estimate confidence", "Low / Medium / High – the owner's rating of how reliable the estimate is. Informational; it does not change any number."),
        new("baseline", "Baseline", "A frozen snapshot of hours and cost taken at activation (v1) and on each approved re-baseline. Variance and EAC are measured against the current baseline."),
        new("actual", "Actual", "Hours and cost imported from timesheets or finance and mapped to this initiative, up to the latest import."),
        new("variance", "Variance", "Actual cost to date minus baseline cost. Negative is under baseline, positive is over. Var % is the same relative to baseline; the initiative flags when it exceeds its variance threshold."),
        new("etc", "Estimate to complete (ETC)", "The remaining planned cost: forecast for work not yet reflected in actuals."),
        new("eac", "Estimate at completion (EAC)", "Actual cost to date plus the estimate to complete, plus the contingency reserve captured on the baseline. Shown once a baseline exists and actuals have been recorded."),
        new("budget", "Approved budget", "The amount approved for this initiative (optionally for a fiscal year), entered on the initiative form."),
        new("remaining", "Remaining", "Approved budget minus the expected total (EAC once actuals exist, otherwise forecast incl. contingency). Negative means over budget; within 10% shows Near budget."),
        new("burn", "Burn", "Actual cost as a percentage of baseline cost (of the live forecast for un-baselined initiatives). The bar fills at 100%; the percentage is shown alongside."),
        new("run-rate", "Monthly / yearly", "Forecast cost spread evenly across the initiative's duration, expressed per month and per year."),
        new("internal-vendor", "Internal / Vendor / Non-labor", "Forecast split by resourcing class: internal FTE labor, vendor labor, and non-labor (software licenses and other costs)."),
        new("capacity-demand", "Planned hours", "Hours allocated to a resource type in a month, summed across all open initiatives."),
        new("capacity-supply", "Supply", "Working hours available in a month for the people of that resource type on the roster (working days × hours/day × head-count)."),
        new("over-allocated", "Over-allocated", "A month where planned hours exceed roster supply for a resource type (shown red). Hatched cells mean nobody of that type is on the roster."),
        new("scenario", "Scenario", "A draft copy of an initiative for what-if planning. Scenarios are excluded from lists, Portfolio and Capacity until promoted."),
        new("flags", "Flags", "Warnings for the row: over variance threshold, over/near budget, unpriced lines, or a pending approval."),
    ];

    private static readonly Dictionary<string, HelpTerm> ByKey = Terms.ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

    public static HelpTerm? Find(string key) => ByKey.TryGetValue(key, out var term) ? term : null;
}
