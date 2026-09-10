using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Application;

/// <summary>Role claim values. Exactly one of these is granted per signed-in user (see <see cref="AppRole"/>).</summary>
public static class AppRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Viewer = "Viewer";

    public static readonly string[] All = [Admin, User, Viewer];

    /// <summary>Entra app roles from before the 3-role model; still honoured when present in a token.</summary>
    public static readonly IReadOnlyDictionary<string, AppRole> Legacy = new Dictionary<string, AppRole>(StringComparer.OrdinalIgnoreCase)
    {
        ["Administrator"] = AppRole.Admin,
        ["InitiativeOwner"] = AppRole.User,
        ["Contributor"] = AppRole.User,
        ["FinancePmo"] = AppRole.User,
        ["Viewer"] = AppRole.Viewer
    };

    public static string Name(AppRole role) => role switch
    {
        AppRole.Admin => Admin,
        AppRole.User => User,
        AppRole.Viewer => Viewer,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    public static AppRole? Parse(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (Enum.TryParse<AppRole>(name, true, out var role) && Enum.IsDefined(role))
        {
            return role;
        }

        return Legacy.TryGetValue(name, out var legacy) ? legacy : null;
    }
}

public static class AppPolicies
{
    /// <summary>Admin area, re-baseline approval, actuals import and user administration.</summary>
    public const string Admin = "Admin";
    /// <summary>Create/edit initiatives (subject to membership), record adjustments.</summary>
    public const string CanEdit = "CanEdit";
    /// <summary>Read everything, export.</summary>
    public const string CanView = "CanView";
}
