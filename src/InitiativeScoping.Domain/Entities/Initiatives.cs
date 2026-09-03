using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

public class Initiative
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public string? SponsoringTeam { get; set; }
    public InitiativeStatus Status { get; set; } = InitiativeStatus.Draft;
    public SizingMethod SizingMethod { get; set; } = SizingMethod.Direct;
    public string? SizeKey { get; set; }
    public DateOnly TargetStart { get; set; }
    public decimal? VarianceThresholdPct { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Phase> Phases { get; set; } = [];
    public List<InitiativeAllocation> Allocations { get; set; } = [];
    public List<InitiativeMember> Members { get; set; } = [];
    public List<ForecastBaseline> Baselines { get; set; } = [];
}

public class InitiativeMember
{
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public required string UserId { get; set; }
    public InitiativeMemberRole Role { get; set; }
}

public class Phase
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public required string Name { get; set; }
    public int Sequence { get; set; }
    public DateOnly PlannedStart { get; set; }
    public DateOnly PlannedEnd { get; set; }
    public List<PhaseDateHistory> DateHistory { get; set; } = [];
}

public class PhaseDateHistory
{
    public int Id { get; set; }
    public int PhaseId { get; set; }
    public Phase? Phase { get; set; }
    public DateOnly OldStart { get; set; }
    public DateOnly OldEnd { get; set; }
    public DateOnly NewStart { get; set; }
    public DateOnly NewEnd { get; set; }
    public required string ChangedBy { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? Reason { get; set; }
}

public class InitiativeAllocation
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int PhaseId { get; set; }
    public Phase? Phase { get; set; }
    public int ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public Seniority Seniority { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public int Quantity { get; set; } = 1;
    public decimal EstimatedHours { get; set; }
    public string? ContractReference { get; set; }
    public string? CostCenter { get; set; }
}

public class ForecastBaseline
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int Version { get; set; }
    public DateTimeOffset SnapshotAt { get; set; }
    public required string SnapshotBy { get; set; }
    public string? Reason { get; set; }
    public bool IsCurrent { get; set; }
    public decimal TotalHours { get; set; }
    public decimal TotalCost { get; set; }
    public List<ForecastBaselineLine> Lines { get; set; } = [];
}

public class ForecastBaselineLine
{
    public int Id { get; set; }
    public int ForecastBaselineId { get; set; }
    public ForecastBaseline? ForecastBaseline { get; set; }
    public int PhaseId { get; set; }
    public int ResourceTypeId { get; set; }
    public Seniority Seniority { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public decimal Hours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Cost { get; set; }
}
