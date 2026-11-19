namespace InitiativeScoping.Web.Areas.Admin.Models;

public static class PodMembershipWarning
{
    public static string? For(string personName, decimal total) => total > 100
        ? $"{personName}: {total:0.##}% total membership across pods (over 100%)."
        : null;

    public static string AfterSave(string personName, decimal total) => For(personName, total) is { } warning
        ? $" Warning: {warning} Adjust membership % on the pod pages."
        : string.Empty;
}
