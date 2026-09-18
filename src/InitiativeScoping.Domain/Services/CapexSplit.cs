namespace InitiativeScoping.Domain.Services;

/// <summary>Apportions a cost between Capex and Opex from a 0–100 Capex percentage. Capex is rounded to cents; Opex is the remainder so the two always sum to the total.</summary>
public static class CapexSplit
{
    public static decimal Capex(decimal cost, decimal capexPercent) =>
        Math.Round(cost * Math.Clamp(capexPercent, 0m, 100m) / 100m, 2, MidpointRounding.AwayFromZero);

    public static decimal Opex(decimal cost, decimal capexPercent) => cost - Capex(cost, capexPercent);

    public static string Label(decimal capexPercent) => capexPercent switch
    {
        <= 0m => "Opex",
        >= 100m => "Capex",
        _ => $"{capexPercent:0.##}% Capex"
    };
}
