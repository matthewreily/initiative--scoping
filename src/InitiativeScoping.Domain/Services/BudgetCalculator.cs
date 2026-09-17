using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

/// <summary>
/// Approved budget against what the initiative is expected to cost. The expected cost is the estimate at completion
/// (spent + remaining plan) once a baseline exists and actuals are flowing, otherwise the live forecast; contingency is included either way.
/// </summary>
public sealed record BudgetPosition(decimal? Budget, string? FiscalYear, decimal Forecast, decimal Eac, decimal Actual, bool UsesEac)
{
    public bool HasBudget => Budget is not null;

    /// <summary>Cost the budget is compared against: EAC when available, otherwise the forecast (both incl. contingency).</summary>
    public decimal Expected => UsesEac ? Eac : Forecast;

    public decimal? Remaining => Budget is null ? null : Budget.Value - Expected;

    /// <summary>Budget not yet spent (ignores the plan; what finance sees as "left in the envelope").</summary>
    public decimal? Unspent => Budget is null ? null : Budget.Value - Actual;

    public bool OverBudget => Remaining is < 0;

    /// <summary>Expected cost as a share of the budget, rounded to one decimal; null without a budget.</summary>
    public decimal? UtilizationPct => Budget is null or <= 0 ? null : Math.Round(Expected / Budget.Value * 100m, 1);

    /// <summary>Within 10% of the budget without exceeding it.</summary>
    public bool NearBudget => !OverBudget && UtilizationPct is >= 90m;
}

public static class BudgetCalculator
{
    public static BudgetPosition Calculate(Initiative initiative, ForecastResult forecast, VarianceResult variance)
    {
        var forecastWithContingency = forecast.TotalCostWithContingency;
        var usesEac = variance.Baseline is not null && variance.ActualCost > 0;
        var eac = usesEac ? variance.EacCost + forecast.ContingencyCost : forecastWithContingency;
        return new BudgetPosition(initiative.ApprovedBudget, initiative.BudgetFiscalYear, forecastWithContingency, eac, variance.ActualCost, usesEac);
    }
}
