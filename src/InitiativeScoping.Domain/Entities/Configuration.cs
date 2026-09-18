using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

public class BusinessUnit
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>External supplier of resources; rate-card rows, allocations and people of a vendor <see cref="ResourcingClass"/> reference one.</summary>
public class Vendor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Admin-managed catalog of how a resource is sourced (e.g. Internal, Vendor). <see cref="IsVendor"/> classes require a
/// <see cref="Vendor"/> on rate-card rows, allocations and people; <see cref="DefaultCapexPercent"/> prefills new labor lines.
/// </summary>
public class ResourcingClass
{
    public const int MaxNameLength = 100;
    /// <summary>Seeded ids: every database starts with these two classes.</summary>
    public const int InternalId = 1;
    public const int VendorId = 2;
    public const string InternalName = "Internal";
    public const string VendorName = "Vendor";
    /// <summary>Name the seeded internal class carried before it became configurable; still accepted in CSV imports.</summary>
    public const string LegacyInternalName = "InternalFte";

    public int Id { get; set; }
    public required string Name { get; set; }
    /// <summary>Resources of this class come from an external supplier, so a vendor is chosen and vendor-specific rates apply.</summary>
    public bool IsVendor { get; set; }
    /// <summary>Capex % a new labor allocation of this class starts with (0–100); the user can still override it per line.</summary>
    public decimal DefaultCapexPercent { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public static bool NameMatches(string name, string value) =>
        string.Equals(name, value, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, InternalName, StringComparison.OrdinalIgnoreCase) && string.Equals(value, LegacyInternalName, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Admin-managed career level (e.g. "Senior", "Level 1 (0-2 Years)"); rate-card rows, allocations, people and templates reference one. Ordered by <see cref="SortOrder"/>.</summary>
public class SeniorityLevel
{
    public const int MaxNameLength = 100;

    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Discipline
{
    public const int MaxNameLength = 100;
    /// <summary>Discipline given to resource types created by CSV import when the file names none.</summary>
    public const string UnassignedName = "Unassigned";

    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResourceType
{
    public const int MaxNameLength = 200;

    public int Id { get; set; }
    public required string Name { get; set; }
    public int DisciplineId { get; set; }
    public Discipline? Discipline { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Global price list shared by every business unit and initiative; entries are keyed by resource type, seniority, location, class and vendor.</summary>
public class RateCard
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly EffectiveStart { get; set; }
    /// <summary>Last day this card prices work; null = open-ended until a later card starts.</summary>
    public DateOnly? EffectiveEnd { get; set; }
    public RateCardStatus Status { get; set; } = RateCardStatus.Draft;
    public List<RateCardEntry> Entries { get; set; } = [];
}

public class RateCardEntry
{
    public int Id { get; set; }
    public int RateCardId { get; set; }
    public RateCard? RateCard { get; set; }
    public int ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public int SeniorityId { get; set; }
    public SeniorityLevel? Seniority { get; set; }
    public required string Location { get; set; }
    public int ResourcingClassId { get; set; }
    public ResourcingClass? ResourcingClass { get; set; }
    /// <summary>Required when the class <see cref="ResourcingClass.IsVendor"/>; null otherwise.</summary>
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public decimal HourlyRate { get; set; }
}

public class SizingConversion
{
    public int Id { get; set; }
    public SizingMethod Method { get; set; }
    public required string Key { get; set; }
    public decimal Hours { get; set; }
}

/// <summary>Single-row settings for converting fixed-duration staffing into hours.</summary>
public class WorkCalendarSettings
{
    public int Id { get; set; }
    public decimal HoursPerDay { get; set; } = 8;
    /// <summary>Calendar month (1–12) in which the fiscal year starts; 1 = fiscal year equals calendar year.</summary>
    public int FiscalYearStartMonth { get; set; } = 1;
}

/// <summary>Admin-managed non-labor cost item (software license, hardware SKU, ...) initiatives can pick to prefill a cost line.</summary>
public class CostCatalogItem
{
    public int Id { get; set; }
    public CostCategory Category { get; set; } = CostCategory.SoftwareLicense;
    public required string Name { get; set; }
    public string? Vendor { get; set; }
    public BillingModel BillingModel { get; set; } = BillingModel.Monthly;
    public decimal UnitCost { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    public decimal CapexPercent { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A non-working weekday excluded from fixed-duration hour calculations.</summary>
public class Holiday
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }
    public required string Name { get; set; }
}

/// <summary>
/// Who may use the application and with which role. Entra ID authenticates; this table authorizes.
/// Created either by an Admin (by e-mail, before the person has signed in) or by the person requesting access.
/// </summary>
public class UserAccount
{
    public int Id { get; set; }
    /// <summary>Entra object id; null until a row added by e-mail is matched at first sign-in.</summary>
    public string? ObjectId { get; set; }
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public AppRole Role { get; set; } = AppRole.Viewer;
    public UserAccountStatus Status { get; set; } = UserAccountStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
    public string? Note { get; set; }
}
