using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

public class BusinessUnit
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ResourceType
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Discipline { get; set; }
    public bool IsActive { get; set; } = true;
}

public class RateCard
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public DateOnly EffectiveStart { get; set; }
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
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public Seniority Seniority { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public decimal HourlyRate { get; set; }
}

public class SizingConversion
{
    public int Id { get; set; }
    public SizingMethod Method { get; set; }
    public required string Key { get; set; }
    public decimal Hours { get; set; }
}
