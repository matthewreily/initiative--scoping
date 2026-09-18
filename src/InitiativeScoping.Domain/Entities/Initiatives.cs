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
    public PlanningMode PlanningMode { get; set; } = PlanningMode.EffortDriven;
    public DateOnly TargetStart { get; set; }
    /// <summary>Fixed end date; required when <see cref="PlanningMode"/> is <see cref="PlanningMode.FixedDuration"/>.</summary>
    public DateOnly? TargetEnd { get; set; }
    public decimal? VarianceThresholdPct { get; set; }
    /// <summary>Risk reserve added on top of the priced forecast, as a percentage of forecast cost (labor + non-labor).</summary>
    public decimal ContingencyPct { get; set; }
    public EstimateConfidence? EstimateConfidence { get; set; }
    /// <summary>Approved funding for the initiative (total, all years). Null when no budget has been approved yet.</summary>
    public decimal? ApprovedBudget { get; set; }
    /// <summary>Label of the budget's fiscal year(s), e.g. "FY26"; free text so it matches the organisation's calendar.</summary>
    public string? BudgetFiscalYear { get; set; }
    /// <summary>Set on what-if scenarios: the initiative whose plan this is an alternative to. Scenarios stay Draft until promoted.</summary>
    public int? ScenarioOfId { get; set; }
    public Initiative? ScenarioOf { get; set; }
    public List<Initiative> Scenarios { get; set; } = [];
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Business units that may supply resources to this initiative; always includes the sponsoring <see cref="BusinessUnitId"/>.</summary>
    public List<InitiativeBusinessUnit> ParticipatingBusinessUnits { get; set; } = [];
    public List<Phase> Phases { get; set; } = [];
    public List<InitiativeAllocation> Allocations { get; set; } = [];
    public List<InitiativeNonLaborCost> NonLaborCosts { get; set; } = [];
    public List<InitiativeMember> Members { get; set; } = [];
    public List<ForecastBaseline> Baselines { get; set; } = [];
    public List<RebaselineRequest> RebaselineRequests { get; set; } = [];
    public List<ActivationRequest> ActivationRequests { get; set; } = [];
    public List<InitiativeNote> Notes { get; set; } = [];
    public List<ChangeRequest> ChangeRequests { get; set; } = [];
    public List<InitiativeSourceMapping> SourceMappings { get; set; } = [];

    public bool IsScenario => ScenarioOfId is not null;

    public ForecastBaseline? CurrentBaseline => Baselines.FirstOrDefault(b => b.IsCurrent);

    /// <summary>An approved re-baseline that has not yet been finalised into a new baseline version.</summary>
    public RebaselineRequest? OpenRebaseline =>
        RebaselineRequests.FirstOrDefault(r => r.Status is RebaselineStatus.Pending or RebaselineStatus.Approved);

    /// <summary>Change requests awaiting an Admin decision.</summary>
    public IEnumerable<ChangeRequest> PendingChangeRequests =>
        ChangeRequests.Where(c => c.Status == ChangeRequestStatus.Pending);

    /// <summary>An activation request awaiting an Admin decision.</summary>
    public ActivationRequest? PendingActivation =>
        ActivationRequests.FirstOrDefault(r => r.Status == ActivationRequestStatus.Pending);

    /// <summary>Sponsor plus explicitly added participants, without duplicates.</summary>
    public IEnumerable<int> ParticipatingBusinessUnitIds =>
        ParticipatingBusinessUnits.Select(p => p.BusinessUnitId).Prepend(BusinessUnitId).Distinct();
}

public class InitiativeBusinessUnit
{
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
}

/// <summary>Owner's request to activate a Draft initiative; an Admin approves (which activates and captures baseline v1) or rejects.</summary>
public class ActivationRequest
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public ActivationRequestStatus Status { get; set; } = ActivationRequestStatus.Pending;
    public string? Reason { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
}

/// <summary>Free-text note on an initiative, optionally attached to one baseline version.</summary>
public class InitiativeNote
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int? ForecastBaselineId { get; set; }
    public ForecastBaseline? ForecastBaseline { get; set; }
    public required string Body { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Owner-initiated, Admin-approved request to unlock scope on an Active initiative and cut a new baseline.</summary>
public class RebaselineRequest
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public RebaselineStatus Status { get; set; } = RebaselineStatus.Pending;
    public required string Reason { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int? ResultingBaselineId { get; set; }
    public ForecastBaseline? ResultingBaseline { get; set; }
}

/// <summary>
/// A formal request to change an Active initiative's scope, schedule, cost or resourcing.
/// Approval by an Admin opens (or joins) a re-baseline; finalising that re-baseline marks the change Implemented
/// and records the resulting baseline so the before/after impact is explainable.
/// </summary>
public class ChangeRequest
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    /// <summary>Sequence within the initiative, shown as CR-1, CR-2, …</summary>
    public int Number { get; set; }
    public ChangeRequestType Type { get; set; }
    public ChangeRequestStatus Status { get; set; } = ChangeRequestStatus.Pending;
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Reason { get; set; }
    /// <summary>Requester's estimate of the cost impact (positive = increase). Null when unknown.</summary>
    public decimal? EstimatedCostImpact { get; set; }
    public decimal? EstimatedHoursImpact { get; set; }
    /// <summary>Requested new target end, when the change moves the schedule.</summary>
    public DateOnly? ProposedTargetEnd { get; set; }
    // Snapshot of the plan when the request was raised.
    public int? BaselineVersionBefore { get; set; }
    public decimal ForecastHoursBefore { get; set; }
    public decimal ForecastCostBefore { get; set; }
    public DateOnly? TargetEndBefore { get; set; }
    // Captured when the linked re-baseline is finalised.
    public decimal? ForecastHoursAfter { get; set; }
    public decimal? ForecastCostAfter { get; set; }
    public DateOnly? TargetEndAfter { get; set; }
    public required string RequestedBy { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public int? RebaselineRequestId { get; set; }
    public RebaselineRequest? RebaselineRequest { get; set; }
    public int? ResultingBaselineId { get; set; }
    public ForecastBaseline? ResultingBaseline { get; set; }

    public string Code => $"CR-{Number}";
    public decimal? ActualCostImpact => ForecastCostAfter is null ? null : ForecastCostAfter - ForecastCostBefore;
    public decimal? ActualHoursImpact => ForecastHoursAfter is null ? null : ForecastHoursAfter - ForecastHoursBefore;
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
    /// <summary>Resourcing business unit used for pricing; one of the initiative's participating BUs.</summary>
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
    public int ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public int SeniorityId { get; set; }
    public SeniorityLevel? Seniority { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public int? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    /// <summary>Named roster people filling seats of this allocation (at most <see cref="Quantity"/>); the rest are generic (type × seniority) seats.</summary>
    public List<InitiativeAllocationPerson> People { get; set; } = [];
    public int Quantity { get; set; } = 1;
    /// <summary>Staffing level per person over the phase window (100 = full time); fixed-duration initiatives only.</summary>
    public decimal? AllocationPercent { get; set; }
    public decimal EstimatedHours { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    public decimal CapexPercent { get; set; }
    public string? ContractReference { get; set; }
    public string? CostCenter { get; set; }

    public IEnumerable<int> PersonIds => People.Select(p => p.PersonId);
    public IEnumerable<Person> NamedPeople => People.Where(p => p.Person is not null).Select(p => p.Person!);
    public bool HasNamedPeople => People.Count > 0;
    /// <summary>Seats not yet filled by a named person.</summary>
    public int UnassignedSeats => Math.Max(0, Quantity - People.Count);
    public string? PersonNames => People.Count == 0 ? null : string.Join(", ", People.Select(p => p.Person?.DisplayName ?? $"Person #{p.PersonId}"));
}

/// <summary>One named seat on an allocation.</summary>
public class InitiativeAllocationPerson
{
    public int Id { get; set; }
    public int AllocationId { get; set; }
    public InitiativeAllocation? Allocation { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }
}

/// <summary>
/// A non-labor cost line (license, hardware, ...). Cost is derived, not stored: UnitCost x Quantity x billable periods
/// over the line's window (explicit dates, else the phase, else the initiative's target window).
/// </summary>
public class InitiativeNonLaborCost
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int? PhaseId { get; set; }
    public Phase? Phase { get; set; }
    public CostCategory Category { get; set; } = CostCategory.SoftwareLicense;
    public int? CostCatalogItemId { get; set; }
    public CostCatalogItem? CostCatalogItem { get; set; }
    public required string Description { get; set; }
    public BillingModel BillingModel { get; set; } = BillingModel.Monthly;
    public int Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    public decimal CapexPercent { get; set; }
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
    public decimal ContingencyPct { get; set; }
    public decimal ContingencyCost { get; set; }
    public EstimateConfidence? EstimateConfidence { get; set; }
    public List<ForecastBaselineLine> Lines { get; set; } = [];
    public List<ForecastBaselineNonLaborLine> NonLaborLines { get; set; } = [];

    public decimal LaborCost => Lines.Sum(l => l.Cost);
    public decimal NonLaborCost => NonLaborLines.Sum(l => l.Cost);
    public decimal TotalCostWithContingency => TotalCost + ContingencyCost;
}

public class ForecastBaselineNonLaborLine
{
    public int Id { get; set; }
    public int ForecastBaselineId { get; set; }
    public ForecastBaseline? ForecastBaseline { get; set; }
    public int? PhaseId { get; set; }

    /// <summary>Phase name when the baseline was taken; null for whole-initiative lines.</summary>
    public string? PhaseName { get; set; }

    public CostCategory Category { get; set; }
    public required string Description { get; set; }
    public BillingModel BillingModel { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int Periods { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    public decimal CapexPercent { get; set; }
    public decimal Cost { get; set; }
}

public class ForecastBaselineLine
{
    public int Id { get; set; }
    public int ForecastBaselineId { get; set; }
    public ForecastBaseline? ForecastBaseline { get; set; }
    public int PhaseId { get; set; }
    public int BusinessUnitId { get; set; }
    public int ResourceTypeId { get; set; }
    public int SeniorityId { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public int? VendorId { get; set; }
    /// <summary>Dimension names as they were when the baseline was taken; later catalog renames do not relabel history.</summary>
    public required string PhaseName { get; set; }
    public required string BusinessUnitName { get; set; }
    public required string ResourceTypeName { get; set; }
    public required string SeniorityName { get; set; }
    public string? VendorName { get; set; }
    public int? PersonId { get; set; }
    public string? PersonName { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    public decimal CapexPercent { get; set; }
    public decimal Hours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal Cost { get; set; }
}
