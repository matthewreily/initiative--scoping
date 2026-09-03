using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InitiativeScoping.Web.Models;

public class InitiativeListItem
{
    public required Initiative Initiative { get; init; }
    public int PhaseCount { get; init; }
    public decimal TotalHours { get; init; }
    public decimal TotalCost { get; init; }
    public bool IsComplete { get; init; }
}

public class InitiativeIndexModel
{
    public required IReadOnlyList<InitiativeListItem> Items { get; init; }
    public InitiativeStatus? Status { get; init; }
    public int? BusinessUnitId { get; init; }
    public string? Search { get; init; }
    public required SelectList BusinessUnits { get; init; }
    public bool CanCreate { get; init; }
}

public class InitiativeEditModel
{
    public int Id { get; set; }
    [Required, StringLength(300)]
    public string Name { get; set; } = string.Empty;
    [StringLength(4000)]
    public string? Description { get; set; }
    [Required, Display(Name = "Business unit")]
    public int BusinessUnitId { get; set; }
    [StringLength(200), Display(Name = "Sponsoring team")]
    public string? SponsoringTeam { get; set; }
    [Required, Display(Name = "Sizing method")]
    public SizingMethod SizingMethod { get; set; } = SizingMethod.Direct;
    [StringLength(50), Display(Name = "Size")]
    public string? SizeKey { get; set; }
    [Required, DataType(DataType.Date), Display(Name = "Target start")]
    public DateOnly TargetStart { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    [Range(0, 1000), Display(Name = "Variance threshold %")]
    public decimal? VarianceThresholdPct { get; set; }
}

public class PhaseEditModel
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    [Required, StringLength(200)]
    public string Name { get; set; } = string.Empty;
    [Required, DataType(DataType.Date), Display(Name = "Planned start")]
    public DateOnly PlannedStart { get; set; }
    [Required, DataType(DataType.Date), Display(Name = "Planned end")]
    public DateOnly PlannedEnd { get; set; }
    [StringLength(500), Display(Name = "Reason for date change")]
    public string? Reason { get; set; }
}

public class AllocationEditModel
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    [Required, Display(Name = "Phase")]
    public int PhaseId { get; set; }
    [Required, Display(Name = "Resource type")]
    public int ResourceTypeId { get; set; }
    [Required]
    public Seniority Seniority { get; set; } = Seniority.Mid;
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Required, Range(1, 1000)]
    public int Quantity { get; set; } = 1;
    [Required, Range(0.25, 1000000), Display(Name = "Hours (each)")]
    public decimal EstimatedHours { get; set; }
    [StringLength(100), Display(Name = "Contract ref")]
    public string? ContractReference { get; set; }
    [StringLength(100), Display(Name = "Cost center")]
    public string? CostCenter { get; set; }
}

public class MemberEditModel
{
    public int InitiativeId { get; set; }
    [Required, StringLength(200), Display(Name = "User id")]
    public string UserId { get; set; } = string.Empty;
    [Required]
    public InitiativeMemberRole Role { get; set; } = InitiativeMemberRole.Contributor;
}

public class ApplySizeModel
{
    public int InitiativeId { get; set; }
    [Required, Display(Name = "Sizing method")]
    public SizingMethod Method { get; set; } = SizingMethod.TShirt;
    [Required, StringLength(50), Display(Name = "Size")]
    public string SizeKey { get; set; } = string.Empty;
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Display(Name = "Replace existing allocations")]
    public bool Replace { get; set; } = true;
}

public sealed record RollupRow(string Label, decimal Hours, decimal Cost, bool HasUnpriced);

public sealed record GanttBar(Phase Phase, double LeftPct, double WidthPct);

public class InitiativeDetailsModel
{
    public required Initiative Initiative { get; init; }
    public required ForecastResult Forecast { get; init; }
    public required IReadOnlyList<RollupRow> ByPhase { get; init; }
    public required IReadOnlyList<RollupRow> ByResourceType { get; init; }
    public required IReadOnlyList<RollupRow> ByClass { get; init; }
    public required IReadOnlyList<GanttBar> Gantt { get; init; }
    public required IReadOnlyDictionary<int, string> ResourceTypeNames { get; init; }
    public required PhaseEditModel NewPhase { get; init; }
    public required AllocationEditModel NewAllocation { get; init; }
    public required MemberEditModel NewMember { get; init; }
    public required ApplySizeModel ApplySize { get; init; }
    public required SelectList Phases { get; init; }
    public required SelectList ResourceTypes { get; init; }
    public required IReadOnlyList<SizingConversion> Conversions { get; init; }
    public bool CanEdit { get; init; }
    public bool CanManage { get; init; }
    public bool ScopeEditable { get; init; }
    public bool ScopeWritable => CanEdit && ScopeEditable;
    public bool CanApproveRebaseline { get; init; }
    public IReadOnlyList<string> ActivationBlockers { get; init; } = [];
    public IReadOnlyList<InitiativeStatus> StatusTransitions { get; init; } = [];
}

public class BaselinesModel
{
    public required Initiative Initiative { get; init; }
    public required IReadOnlyList<ForecastBaseline> Baselines { get; init; }
    public ForecastBaseline? Selected { get; init; }
    public ForecastBaseline? Previous { get; init; }
    public required ForecastResult LiveForecast { get; init; }
    public required IReadOnlyList<BaselineLineRow> Lines { get; init; }
    public required IReadOnlyList<RebaselineRequest> Requests { get; init; }
    public bool CanManage { get; init; }
    public bool CanApprove { get; init; }
}

public sealed record BaselineLineRow(
    string Phase,
    string ResourceType,
    Seniority Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    decimal Hours,
    decimal HourlyRate,
    decimal Cost,
    decimal? HoursDelta,
    decimal? CostDelta);
