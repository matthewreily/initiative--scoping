using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public readonly record struct RunRate(decimal Monthly, decimal Yearly, int Days);

/// <summary>
/// Average spend rate of an initiative: total forecast cost spread evenly over its planned window
/// (target start → <see cref="NonLaborCostCalculator.InitiativeEnd"/>, inclusive), expressed per average month and per year.
/// </summary>
public static class RunRateCalculator
{
    private const decimal DaysPerYear = 365.25m;
    private const decimal DaysPerMonth = DaysPerYear / 12;

    public static RunRate Calculate(Initiative initiative, decimal totalCost)
    {
        var days = NonLaborCostCalculator.InitiativeEnd(initiative).DayNumber - initiative.TargetStart.DayNumber + 1;
        var perDay = totalCost / days;
        return new RunRate(Round(perDay * DaysPerMonth), Round(perDay * DaysPerYear), days);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
