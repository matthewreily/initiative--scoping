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

public enum InitiativeMemberRole
{
    Owner = 1,
    Contributor = 2,
    Viewer = 3
}
