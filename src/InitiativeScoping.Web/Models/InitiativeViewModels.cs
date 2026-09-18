using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InitiativeScoping.Web.Models;

public class InitiativeListItem
{
    public required Initiative Initiative { get; init; }
    public int PhaseCount { get; init; }
    public decimal TotalHours { get; init; }
    public decimal TotalCost { get; init; }
    public RunRate RunRate { get; init; }
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

/// <summary>A size an initiative may pick: one that has an allocation template (and, when configured, a conversion to hours).</summary>
public record SizeOption(SizingMethod Method, string Key, decimal? Hours)
{
    public string Label => Hours is null ? Key : $"{Key} ({Hours:0.#} h)";
}

public class InitiativeEditModel
{
    public int Id { get; set; }
    [Required, StringLength(300)]
    public string Name { get; set; } = string.Empty;
    [StringLength(4000)]
    public string? Description { get; set; }
    [Required, Display(Name = "Sponsoring business unit")]
    public int BusinessUnitId { get; set; }
    /// <summary>Additional business units allowed to supply resources; the sponsor is always included.</summary>
    [Display(Name = "Participating business units")]
    public List<int> ParticipatingBusinessUnitIds { get; set; } = [];
    [StringLength(200), Display(Name = "Sponsoring team")]
    public string? SponsoringTeam { get; set; }
    [Required, Display(Name = "Sizing method")]
    public SizingMethod SizingMethod { get; set; } = SizingMethod.TShirt;
    [StringLength(50), Display(Name = "Size")]
    public string? SizeKey { get; set; }
    [Required, Display(Name = "Planning mode")]
    public PlanningMode PlanningMode { get; set; } = PlanningMode.EffortDriven;
    [Required, DataType(DataType.Date), Display(Name = "Target start")]
    public DateOnly TargetStart { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    [DataType(DataType.Date), Display(Name = "Target end")]
    public DateOnly? TargetEnd { get; set; }
    [Range(0, 1000), Display(Name = "Variance threshold %")]
    public decimal? VarianceThresholdPct { get; set; }
    [Range(0, 100), Display(Name = "Contingency %")]
    public decimal ContingencyPct { get; set; }
    [Display(Name = "Estimate confidence")]
    public EstimateConfidence? EstimateConfidence { get; set; }
    [Range(0, 999_999_999_999.99), Display(Name = "Approved budget")]
    public decimal? ApprovedBudget { get; set; }
    [StringLength(50), Display(Name = "Budget fiscal year")]
    public string? BudgetFiscalYear { get; set; }
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
    [Required, Display(Name = "Business unit")]
    public int BusinessUnitId { get; set; }
    [Required, Display(Name = "Resource type")]
    public int ResourceTypeId { get; set; }
    [Required, Display(Name = "Seniority")]
    public int SeniorityId { get; set; }
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Display(Name = "Vendor")]
    public int? VendorId { get; set; }
    /// <summary>Named roster people, one seat each; at most <see cref="Quantity"/>.</summary>
    [Display(Name = "People")]
    public List<int> PersonIds { get; set; } = [];
    [Required, Range(1, 1000)]
    public int Quantity { get; set; } = 1;
    /// <summary>Entered directly in effort-driven mode; computed from <see cref="AllocationPercent"/> in fixed-duration mode.</summary>
    [Range(0, 1000000), Display(Name = "Hours (each)")]
    public decimal EstimatedHours { get; set; }
    [Range(0.01, 100), Display(Name = "Allocation %")]
    public decimal? AllocationPercent { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    [Required, Range(0, 100), Display(Name = "Capex %")]
    public decimal CapexPercent { get; set; }
    [StringLength(100), Display(Name = "Contract ref")]
    public string? ContractReference { get; set; }
    [StringLength(100), Display(Name = "Cost center")]
    public string? CostCenter { get; set; }
}

public class NonLaborCostEditModel
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    /// <summary>Null = whole initiative window.</summary>
    [Display(Name = "Phase")]
    public int? PhaseId { get; set; }
    [Display(Name = "Catalog item")]
    public int? CostCatalogItemId { get; set; }
    [Required]
    public CostCategory Category { get; set; } = CostCategory.SoftwareLicense;
    [Required, StringLength(300)]
    public string Description { get; set; } = string.Empty;
    [Required, Display(Name = "Billing")]
    public BillingModel BillingModel { get; set; } = BillingModel.Monthly;
    [Required, Range(1, 100000)]
    public int Quantity { get; set; } = 1;
    [Required, Range(0, 999_999_999), Display(Name = "Unit cost")]
    public decimal? UnitCost { get; set; }
    [Display(Name = "Start")]
    public DateOnly? StartDate { get; set; }
    [Display(Name = "End")]
    public DateOnly? EndDate { get; set; }
    /// <summary>Share of the cost that is capitalised, 0–100; the remainder is Opex.</summary>
    [Required, Range(0, 100), Display(Name = "Capex %")]
    public decimal CapexPercent { get; set; }
    [StringLength(200), Display(Name = "Contract ref")]
    public string? ContractReference { get; set; }
    [StringLength(100), Display(Name = "Cost center")]
    public string? CostCenter { get; set; }
}

/// <summary>Inputs for the shared fiscal-year / quarter subtotal table.</summary>
public sealed record FiscalPeriodsViewModel(MonthlyPhasing Phasing, FiscalCalendar Calendar);

/// <summary>Catalog item as offered to the initiative form; the client prefills description/billing/unit cost from it.</summary>
public sealed record CatalogOption(int Id, CostCategory Category, string Name, string? Vendor, BillingModel BillingModel, decimal UnitCost, decimal CapexPercent = 0m)
{
    public string Label => Vendor is null ? Name : $"{Name} ({Vendor})";
}

/// <summary>Inputs for the live cost preview script. <paramref name="Windows"/>: phase id → planned window, 0 → initiative window (used when the line has no explicit dates).</summary>
public sealed record CostPreviewScriptModel(string Prefix, IReadOnlyList<CatalogOption> Catalog, IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> Windows);

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
    [Required, Display(Name = "Business unit")]
    public int BusinessUnitId { get; set; }
    [Required, StringLength(100)]
    public string Location { get; set; } = "Onshore";
    [Required, Display(Name = "Class")]
    public ResourcingClass ResourcingClass { get; set; } = ResourcingClass.InternalFte;
    [Display(Name = "Vendor")]
    public int? VendorId { get; set; }
    [Display(Name = "Replace existing allocations")]
    public bool Replace { get; set; } = true;
}

public sealed record RollupRow(string Label, decimal Hours, decimal Cost, bool HasUnpriced);

public sealed record GanttBar(Phase Phase, double LeftPct, double WidthPct);

/// <summary>One priced (resource type, seniority, location, class, vendor) combination from a published rate card; rates are global across business units.</summary>
public sealed record RateOption(int ResourceTypeId, string ResourceType, int SeniorityId, string Location, ResourcingClass ResourcingClass, int? VendorId, decimal Rate);

public sealed record RateCardOptions(int CardId, DateOnly EffectiveStart, DateOnly? EffectiveEnd, IReadOnlyList<RateOption> Options);

/// <summary>Data for the allocation form: priced combinations per published card, and phase start dates to pick the effective card.</summary>
public sealed record RateOptionsScriptModel(
    string PhaseSelectId,
    string ResourceTypeSelectId,
    string SenioritySelectId,
    string LocationSelectId,
    string ClassSelectId,
    string RateOutputId,
    RateOptionsData Data,
    AllocationEditModel Current)
{
    public string VendorSelectId { get; init; } = "VendorId";
    public string BusinessUnitSelectId { get; init; } = "BusinessUnitId";
    public string PersonSelectId { get; init; } = "PersonIds";
    public string QuantityInputId { get; init; } = "Quantity";
}

/// <summary>Global priced combinations per published card, the initiative's participating BUs (for allocation ownership), plus the catalogs needed for the unpriced fallback.</summary>
public sealed record RateOptionsData(
    IReadOnlyList<NamedId> BusinessUnits,
    int SponsorBusinessUnitId,
    IReadOnlyList<RateCardOptions> Cards,
    IReadOnlyDictionary<int, DateOnly> PhaseStarts,
    IReadOnlyList<NamedId> AllResourceTypes,
    IReadOnlyList<string> AllLocations,
    IReadOnlyList<NamedId> Vendors,
    IReadOnlyList<NamedId> Seniorities,
    IReadOnlyList<PersonOption> People)
{
    public bool HasAnyPricing => Cards.Any(c => c.Options.Count > 0);
}

/// <summary>Roster person selectable on an allocation; the form only offers those matching the chosen dimensions.</summary>
public sealed record PersonOption(int Id, string Name, int ResourceTypeId, int SeniorityId, int BusinessUnitId, ResourcingClass ResourcingClass, int? VendorId);

public sealed record NamedId(int Id, string Name);

public sealed record ComputedHoursScriptModel(string PhaseSelectId, string PercentInputId, string OutputId, IReadOnlyDictionary<int, int> PhaseWorkingDays, decimal HoursPerDay);

/// <summary>Fixed-duration schedule summary shown on the Details page.</summary>
public sealed record FixedDurationSummary(DateOnly Start, DateOnly End, int CalendarDays, int WorkingDays, decimal HoursPerDay, decimal AverageFte, IReadOnlyDictionary<int, int> PhaseWorkingDays)
{
    public decimal Weeks => Math.Round(CalendarDays / 7m, 1);
}

public class InitiativeDetailsModel
{
    public required Initiative Initiative { get; init; }
    public required ForecastResult Forecast { get; init; }
    public required IReadOnlyList<RollupRow> ByPhase { get; init; }
    public required IReadOnlyList<RollupRow> ByResourceType { get; init; }
    public required IReadOnlyList<RollupRow> ByClass { get; init; }
    public required IReadOnlyList<RollupRow> ByBusinessUnit { get; init; }
    public required IReadOnlyList<RollupRow> ByVendor { get; init; }
    public required IReadOnlyList<GanttBar> Gantt { get; init; }
    public FixedDurationSummary? FixedDuration { get; init; }
    public required IReadOnlyDictionary<int, string> ResourceTypeNames { get; init; }
    public required PhaseEditModel NewPhase { get; init; }
    public required AllocationEditModel NewAllocation { get; init; }
    public required NonLaborCostEditModel NewNonLaborCost { get; init; }
    public required IReadOnlyList<CatalogOption> CatalogOptions { get; init; }
    public required IReadOnlyDictionary<int, (DateOnly Start, DateOnly End)> CostPreviewWindows { get; init; }
    public required MemberEditModel NewMember { get; init; }
    public required ApplySizeModel ApplySize { get; init; }
    public required SelectList Phases { get; init; }
    public required RateOptionsData RateOptions { get; init; }
    public required IReadOnlyList<SizeOption> SizeOptions { get; init; }
    public bool CanEdit { get; init; }
    public bool CanManage { get; init; }
    public bool ScopeEditable { get; init; }
    public bool ScopeWritable => CanEdit && ScopeEditable;
    public bool CanApproveRebaseline { get; init; }
    public bool CanApproveActivation { get; init; }
    public bool CanAddNote { get; init; }
    public IReadOnlyList<string> ActivationBlockers { get; init; } = [];
    public IReadOnlyList<InitiativeStatus> StatusTransitions { get; init; } = [];
    public required VarianceResult Variance { get; init; }
    public required MonthlyPhasing Phasing { get; init; }
    public FiscalCalendar Fiscal { get; init; } = FiscalCalendar.Calendar;
    public int UnmappedForMappedProjects { get; init; }
    public BudgetPosition Budget => BudgetCalculator.Calculate(Initiative, Forecast, Variance);
}

public class InitiativeExplainModel
{
    public required Initiative Initiative { get; init; }
    public required ForecastResult Forecast { get; init; }
    public required IReadOnlyList<Phase> Phases { get; init; }
    public required IReadOnlyDictionary<int, string> ResourceTypeNames { get; init; }
    public decimal HoursPerDay { get; init; }
    public required IReadOnlyDictionary<int, int> PhaseWorkingDays { get; init; }
    public ForecastBaseline? CurrentBaseline { get; init; }
    public bool FixedDuration => Initiative.PlanningMode == PlanningMode.FixedDuration;
}

public class InitiativeActualsModel
{
    public required Initiative Initiative { get; init; }
    public required VarianceResult Variance { get; init; }
    public required IReadOnlyList<ActualEntry> Entries { get; init; }
    public required IReadOnlyList<ActualAdjustment> Adjustments { get; init; }
    public required AdjustmentEditModel NewAdjustment { get; init; }
    public bool CanManage { get; init; }
}

public class AdjustmentEditModel
{
    public int InitiativeId { get; set; }
    [Required]
    public CostCategory Category { get; set; } = CostCategory.Labor;
    [Required, Range(-1000000, 1000000)]
    public decimal Hours { get; set; }
    [Required, Range(-1000000000, 1000000000)]
    public decimal Cost { get; set; }
    [Required, StringLength(1000)]
    public string Reason { get; set; } = string.Empty;
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
    public IReadOnlyList<InitiativeNote> Notes { get; init; } = [];
    public bool CanManage { get; init; }
    public bool CanApprove { get; init; }
    public bool CanAddNote { get; init; }
}

public class NotesPanelModel
{
    public int InitiativeId { get; init; }
    public int? BaselineId { get; init; }
    public required IReadOnlyList<InitiativeNote> Notes { get; init; }
    public bool CanAdd { get; init; }
    public string? ReturnUrl { get; init; }
    public string Heading { get; init; } = "Notes";
    public string EmptyText { get; init; } = "No notes yet.";
}

public class ApprovalsModel
{
    public required IReadOnlyList<ActivationRequest> Activations { get; init; }
    public required IReadOnlyList<RebaselineRequest> Rebaselines { get; init; }
    public IReadOnlyList<ChangeRequest> Changes { get; init; } = [];
}

public class ChangeRequestEditModel
{
    [Required]
    public ChangeRequestType Type { get; set; } = ChangeRequestType.Scope;
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    [Required, StringLength(4000)]
    public string Description { get; set; } = string.Empty;
    [Required, StringLength(2000)]
    public string Reason { get; set; } = string.Empty;
    [Display(Name = "Estimated cost impact")]
    public decimal? EstimatedCostImpact { get; set; }
    [Display(Name = "Estimated hours impact")]
    public decimal? EstimatedHoursImpact { get; set; }
    [Display(Name = "Proposed target end")]
    public DateOnly? ProposedTargetEnd { get; set; }
}

public sealed record BaselineLineRow(
    string Phase,
    string BusinessUnit,
    string? Vendor,
    string ResourceType,
    string Seniority,
    string Location,
    ResourcingClass ResourcingClass,
    decimal Hours,
    decimal HourlyRate,
    decimal Cost,
    decimal? HoursDelta,
    decimal? CostDelta)
{
    public string? Person { get; init; }
}

public class PortfolioModel
{
    public required PortfolioResult Portfolio { get; init; }
    public InitiativeStatus? Status { get; init; }
    public int? BusinessUnitId { get; init; }
    public bool IncludeClosed { get; init; }
    public required SelectList BusinessUnits { get; init; }
    public bool CanExport { get; init; }
    public required IReadOnlyList<string> Formats { get; init; }
    public FiscalCalendar Fiscal { get; init; } = FiscalCalendar.Calendar;

    /// <summary>The initiatives on the current page, in the requested order; totals still come from <see cref="Portfolio"/>.</summary>
    public required IReadOnlyList<PortfolioRow> PageRows { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public string Sort { get; init; } = PortfolioSort.Default;
    public bool Desc { get; init; }

    private object RouteValues(int page, int size, string? sort = null, bool? desc = null) => new
    {
        status = Status, businessUnitId = BusinessUnitId, includeClosed = IncludeClosed ? "true" : null,
        sort = (sort ?? Sort) == PortfolioSort.Default ? null : sort ?? Sort,
        dir = (desc ?? Desc) ? "desc" : null,
        page = page == 1 ? null : (int?)page,
        size = size == PortfolioSort.DefaultPageSize ? null : (int?)size
    };

    public PagerModel Pager(IUrlHelper url) =>
        new(Page, PageSize, Portfolio.Count, (page, size) => url.Action("Index", "Portfolio", RouteValues(page, size))!);

    /// <summary>Link that sorts by <paramref name="key"/>: toggles direction when already sorted by it, resets to page 1.</summary>
    public string SortUrl(IUrlHelper url, string key) =>
        url.Action("Index", "Portfolio", RouteValues(1, PageSize, key, key == Sort && !Desc))!;

    public string? AriaSort(string key) => key != Sort ? null : Desc ? "descending" : "ascending";
    public string SortClass(string key) => key != Sort ? "" : Desc ? "sorted-desc" : "sorted-asc";
}

/// <summary>Server-side sort keys for the Portfolio table (query string <c>sort</c>).</summary>
public static class PortfolioSort
{
    public const string Default = "bu";
    public const int DefaultPageSize = 25;

    private static readonly Dictionary<string, Func<PortfolioRow, IComparable?>> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["initiative"] = r => r.Initiative.Name,
        ["bu"] = r => r.Initiative.BusinessUnit?.Name,
        ["status"] = r => r.Initiative.Status,
        ["forecast"] = r => r.ForecastCost,
        ["contingency"] = r => r.ContingencyCost,
        ["baseline"] = r => r.HasBaseline ? r.BaselineCost : null,
        ["actual"] = r => r.ActualCost,
        ["variance"] = r => r.HasBaseline ? r.CostVariance : null,
        ["varpct"] = r => r.CostVariancePct,
        ["eac"] = r => r.HasBaseline ? r.EacCost : null,
        ["budget"] = r => r.ApprovedBudget,
        ["remaining"] = r => r.BudgetRemaining,
        ["burn"] = r => r.BurnPct
    };

    public static string Normalize(string? sort) =>
        sort is not null && Keys.ContainsKey(sort) ? sort.ToLowerInvariant() : Default;

    /// <summary>Sorts rows by <paramref name="sort"/>; rows without a value always sink to the bottom, ties fall back to BU then name.</summary>
    public static IReadOnlyList<PortfolioRow> Apply(IReadOnlyList<PortfolioRow> rows, string sort, bool desc)
    {
        var key = Keys[Normalize(sort)];
        var present = rows.Where(r => key(r) is not null);
        var ordered = desc ? present.OrderByDescending(r => key(r)) : present.OrderBy(r => key(r));
        return ordered
            .ThenBy(r => r.Initiative.BusinessUnit?.Name)
            .ThenBy(r => r.Initiative.Name)
            .Concat(rows.Where(r => key(r) is null))
            .ToList();
    }
}

public class CapacityModel
{
    public required CapacityHeatmap Heatmap { get; init; }
    public CapacityView View { get; init; }
    public InitiativeStatus? Status { get; init; }
    public int? BusinessUnitId { get; init; }
    public bool IncludeClosed { get; init; }
    public decimal HoursPerDay { get; init; }
    public required SelectList BusinessUnits { get; init; }
    public required IReadOnlyList<string> Formats { get; init; }
}

public enum CapacityView
{
    ResourceType,
    People
}

public class NewScenarioModel
{
    public int InitiativeId { get; set; }

    [Required, StringLength(300), Display(Name = "Scenario name")]
    public string? Name { get; set; }
}

public class ScenarioCompareModel
{
    public required Initiative Parent { get; init; }
    public required ScenarioComparison Comparison { get; init; }
    public required IReadOnlyDictionary<int, string> ResourceTypeNames { get; init; }
    public required NewScenarioModel NewScenario { get; init; }
    public bool CanEdit { get; init; }
    public bool CanPromote { get; init; }
    /// <summary>Print layout: no links, forms or action buttons in the comparison table.</summary>
    public bool Printable { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public class InitiativeOnePagerModel
{
    public required Initiative Initiative { get; init; }
    public required ForecastResult Forecast { get; init; }
    public required IReadOnlyList<Phase> Phases { get; init; }
    public required IReadOnlyList<RollupRow> ByPhase { get; init; }
    public required IReadOnlyList<RollupRow> ByResourceType { get; init; }
    public required IReadOnlyList<RollupRow> ByClass { get; init; }
    public required IReadOnlyList<RollupRow> ByPerson { get; init; }
    public required VarianceResult Variance { get; init; }
    public required MonthlyPhasing Phasing { get; init; }
    public FiscalCalendar Fiscal { get; init; } = FiscalCalendar.Calendar;
    public required DateTimeOffset GeneratedAt { get; init; }
    public BudgetPosition Budget => BudgetCalculator.Calculate(Initiative, Forecast, Variance);
    public DateOnly? PlanStart => Phases.Count == 0 ? null : Phases.Min(p => p.PlannedStart);
    public DateOnly? PlanEnd => Phases.Count == 0 ? null : Phases.Max(p => p.PlannedEnd);
}
