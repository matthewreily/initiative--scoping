namespace InitiativeScoping.Web.Models;

public class HomeViewModel
{
    public required string UserDisplayName { get; init; }
    public int BusinessUnitCount { get; init; }
    public int ResourceTypeCount { get; init; }
    public int PublishedRateCardCount { get; init; }
    public int InitiativeCount { get; init; }
    public IReadOnlyList<SetupStep> SetupSteps { get; init; } = [];
    public bool ShowSetupChecklist => SetupSteps.Count > 0 && SetupSteps.Any(s => !s.Done);
}

public record SetupStep(string Title, string Hint, bool Done, string Area, string Controller, string Action);
