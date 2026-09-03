namespace InitiativeScoping.Application;

public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string InitiativeOwner = "InitiativeOwner";
    public const string Contributor = "Contributor";
    public const string Viewer = "Viewer";
    public const string FinancePmo = "FinancePmo";

    public static readonly string[] All = [Administrator, InitiativeOwner, Contributor, Viewer, FinancePmo];
}

public static class AppPolicies
{
    public const string Admin = "Admin";
    public const string CanEditInitiatives = "CanEditInitiatives";
    public const string CanView = "CanView";
    public const string CanExport = "CanExport";
    public const string CanManageActuals = "CanManageActuals";
}
