namespace InitiativeScoping.Domain.Services;

public static class ContingencyCalculator
{
    public const decimal MaxPct = 100m;

    /// <summary>Reserve = cost x pct / 100, rounded to cents; never negative.</summary>
    public static decimal Reserve(decimal cost, decimal pct)
    {
        if (cost <= 0 || pct <= 0)
        {
            return 0m;
        }

        return Math.Round(cost * pct / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
