using System.ComponentModel.DataAnnotations;

namespace InitiativeScoping.Domain.Enums;

public enum ResourcingClass
{
    InternalFte = 1,
    Vendor = 2
}

public enum Seniority
{
    Associate = 1,
    Mid = 2,
    Senior = 3,
    Staff = 4,
    Principal = 5
}

public enum RateCardStatus
{
    Draft = 1,
    Published = 2,
    Retired = 3
}

public enum InitiativeStatus
{
    Draft = 1,
    Active = 2,
    OnHold = 3,
    Complete = 4,
    Cancelled = 5
}

public enum SizingMethod
{
    Direct = 1,
    TShirt = 2,
    StoryPoints = 3
}

public enum RebaselineStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Completed = 4,
    Withdrawn = 5
}

public enum InitiativeMemberRole
{
    Owner = 1,
    Contributor = 2,
    Viewer = 3
}

public enum PlanningMode
{
    EffortDriven = 1,
    FixedDuration = 2
}

/// <summary>What a cost line pays for. Labor is priced from rate cards; the rest are non-labor lines.</summary>
public enum CostCategory
{
    Labor = 1,
    [Display(Name = "Software license")]
    SoftwareLicense = 2,
    Hardware = 3,
    Cloud = 4,
    Travel = 5,
    Other = 6
}

/// <summary>How a non-labor unit cost recurs. Recurring models bill whole periods: any partial period counts as a full one.</summary>
public enum BillingModel
{
    [Display(Name = "One-time")]
    OneTime = 1,
    Monthly = 2,
    Annual = 3
}
