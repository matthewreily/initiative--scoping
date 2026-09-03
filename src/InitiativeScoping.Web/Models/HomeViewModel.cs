namespace InitiativeScoping.Web.Models;

public class HomeViewModel
{
    public required string UserDisplayName { get; init; }
    public int BusinessUnitCount { get; init; }
    public int ResourceTypeCount { get; init; }
    public int PublishedRateCardCount { get; init; }
    public int InitiativeCount { get; init; }
}
