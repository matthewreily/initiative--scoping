using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Application.Exports;

public static class PortfolioExport
{
    public static IReadOnlyList<ExportTable> Build(PortfolioResult portfolio, FiscalCalendar? fiscal = null)
    {
        fiscal ??= FiscalCalendar.Calendar;
        var initiatives = new ExportTable("Initiatives",
            ["Id", "Initiative", "Business unit", "Status", "Target start", "Baseline version",
             "Forecast hours", "Forecast cost", "Internal forecast cost", "Vendor forecast cost", "Non-labor forecast cost",
             "Contingency %", "Contingency cost", "Forecast cost with contingency", "Estimate confidence",
             "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Cost variance", "Cost variance %",
             "ETC cost", "EAC cost", "Projected variance", "Projected variance %",
             "Approved budget", "Budget fiscal year", "Budget remaining", "Budget used %", "Over budget",
             "Threshold %", "Over threshold", "Unpriced forecast", "Unpriced actuals", "Planning mode", "Target end",
             "Pending change requests", "Approved change requests", "Implemented change requests", "Pending change cost impact"],
            portfolio.Rows.Select(r => (IReadOnlyList<object?>)
            [
                r.Initiative.Id, r.Initiative.Name, r.Initiative.BusinessUnit?.Name, r.Initiative.Status.ToString(), r.Initiative.TargetStart, r.BaselineVersion,
                r.ForecastHours, r.ForecastCost, r.InternalForecastCost, r.VendorForecastCost, r.NonLaborForecastCost,
                r.ContingencyPct, r.ContingencyCost, r.ForecastCostWithContingency, r.Confidence?.ToString(),
                r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.CostVariance, r.CostVariancePct,
                r.Variance.EtcCost, r.Variance.EacCost, r.Variance.EacCostVariance, r.Variance.EacCostVariancePct,
                r.ApprovedBudget, r.Budget.FiscalYear, r.BudgetRemaining, r.Budget.UtilizationPct, r.Budget.HasBudget ? r.OverBudget : null,
                r.Variance.ThresholdPct, r.ExceedsThreshold, r.HasUnpricedForecast, r.HasUnpricedActuals,
                r.Initiative.PlanningMode.ToString(), r.Initiative.TargetEnd,
                r.PendingChangeRequests, r.ApprovedChangeRequests, r.ImplementedChangeRequests, r.PendingChangeCostImpact
            ]).ToList());

        return
        [
            initiatives,
            ChangeRequests(portfolio.Rows),
            Groups("By sponsor business unit", portfolio.ByBusinessUnit),
            Groups("By status", portfolio.ByStatus),
            LaborSplit("By resourcing business unit", "Business unit", portfolio.ByResourcingBusinessUnit),
            LaborSplit("By vendor", "Vendor", portfolio.ByVendor),
            MonthlyPhasingExport.Table("By month", portfolio.ByMonth),
            MonthlyPhasingExport.FiscalTable("By fiscal period", portfolio.ByMonth, fiscal),
            InitiativeMonths(portfolio.Rows),
            InitiativeFiscalPeriods(portfolio.Rows, fiscal)
        ];
    }

    private static ExportTable ChangeRequests(IReadOnlyList<PortfolioRow> rows) =>
        new("Change requests",
            ["Id", "Initiative", .. ChangeRequestExport.Headers],
            rows.SelectMany(r => r.Initiative.ChangeRequests.OrderBy(c => c.Number).Select(c => (IReadOnlyList<object?>)[r.Initiative.Id, r.Initiative.Name, .. ChangeRequestExport.Row(c)])).ToList());

    private static ExportTable InitiativeFiscalPeriods(IReadOnlyList<PortfolioRow> rows, FiscalCalendar fiscal) =>
        new("Initiative by fiscal period",
            ["Id", "Initiative", "Fiscal year", "Quarter", "Period", "Start", "End", "Forecast hours", "Forecast labor cost", "Forecast non-labor cost", "Forecast cost", "Forecast capex", "Forecast opex", "Baseline cost", "Baseline capex", "Baseline opex", "Forecast vs. baseline", "Actual cost"],
            rows.SelectMany(r => r.Phasing.ByFiscalPeriod(fiscal).Select(p => (IReadOnlyList<object?>)
                [r.Initiative.Id, r.Initiative.Name, p.FiscalYear, p.Quarter, p.Label, p.Start, p.End, p.ForecastHours, p.ForecastLaborCost, p.ForecastNonLaborCost, p.ForecastCost, p.ForecastCapexCost, p.ForecastOpexCost, p.BaselineCost, p.BaselineCapexCost, p.BaselineOpexCost, p.VarianceToBaseline, p.ActualCost])).ToList());

    private static ExportTable InitiativeMonths(IReadOnlyList<PortfolioRow> rows) =>
        new("Initiative by month",
            ["Id", "Initiative", "Month", "Forecast hours", "Forecast labor cost", "Forecast non-labor cost", "Forecast cost", "Baseline cost", "Actual cost"],
            rows.SelectMany(r => r.Phasing.Months.Select(m => (IReadOnlyList<object?>)
                [r.Initiative.Id, r.Initiative.Name, m.Month, m.ForecastHours, m.ForecastLaborCost, m.ForecastNonLaborCost, m.ForecastCost, m.BaselineCost, m.ActualCost])).ToList());

    private static ExportTable LaborSplit(string name, string keyHeader, IReadOnlyList<LaborSplitGroup> groups) =>
        new(name,
            [keyHeader, "Initiatives", "Hours", "Forecast labor cost", "Has unpriced"],
            groups.Select(g => (IReadOnlyList<object?>)[g.Label, g.Initiatives, g.Hours, g.ForecastCost, g.HasUnpriced]).ToList());

    private static ExportTable Groups(string name, IReadOnlyList<PortfolioGroup> groups) =>
        new(name,
            ["Group", "Initiatives", "Forecast cost", "Baseline cost", "Actual cost", "Cost variance", "Cost variance %", "Over threshold"],
            groups.Select(g => (IReadOnlyList<object?>)[g.Label, g.Count, g.ForecastCost, g.BaselineCost, g.ActualCost, g.CostVariance, g.CostVariancePct, g.OverThreshold]).ToList());
}

public static class MonthlyPhasingExport
{
    public static ExportTable Table(string name, MonthlyPhasing phasing) =>
        new(name,
            ["Month", "Forecast hours", "Forecast labor cost", "Forecast non-labor cost", "Forecast cost", "Forecast capex", "Forecast opex", "Baseline cost", "Baseline capex", "Baseline opex", "Forecast vs. baseline", "Actual cost", "Cumulative forecast", "Cumulative baseline", "Cumulative actual"],
            phasing.Months.Select(m => (IReadOnlyList<object?>)
            [
                m.Month, m.ForecastHours, m.ForecastLaborCost, m.ForecastNonLaborCost, m.ForecastCost, m.ForecastCapexCost, m.ForecastOpexCost,
                m.BaselineCost, m.BaselineCapexCost, m.BaselineOpexCost, m.VarianceToBaseline, m.ActualCost,
                m.CumulativeForecastCost, m.CumulativeBaselineCost, m.CumulativeActualCost
            ]).ToList());

    public static ExportTable FiscalTable(string name, MonthlyPhasing phasing, FiscalCalendar fiscal) =>
        new(name,
            ["Fiscal year", "Quarter", "Period", "Start", "End", "Forecast hours", "Forecast labor cost", "Forecast non-labor cost", "Forecast cost", "Forecast capex", "Forecast opex", "Baseline cost", "Baseline capex", "Baseline opex", "Forecast vs. baseline", "Actual cost"],
            phasing.ByFiscalPeriod(fiscal).Select(p => (IReadOnlyList<object?>)
            [
                p.FiscalYear, p.Quarter, p.Label, p.Start, p.End, p.ForecastHours, p.ForecastLaborCost, p.ForecastNonLaborCost, p.ForecastCost, p.ForecastCapexCost, p.ForecastOpexCost,
                p.BaselineCost, p.BaselineCapexCost, p.BaselineOpexCost, p.VarianceToBaseline, p.ActualCost
            ]).ToList());
}

public static class InitiativeExport
{
    public static IReadOnlyList<ExportTable> Build(
        Initiative initiative,
        ForecastResult forecast,
        VarianceResult variance,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments,
        MonthlyPhasing phasing,
        IReadOnlyDictionary<int, string> resourceTypeNames,
        IReadOnlyDictionary<int, string> businessUnitNames,
        IReadOnlyDictionary<int, string> vendorNames,
        IReadOnlyDictionary<int, string> seniorityNames,
        FiscalCalendar? fiscal = null)
    {
        fiscal ??= FiscalCalendar.Calendar;
        var phases = initiative.Phases.ToDictionary(p => p.Id, p => p.Name);
        var baseline = variance.Baseline;
        var budget = BudgetCalculator.Calculate(initiative, forecast, variance);

        var summary = new ExportTable("Summary", ["Field", "Value"],
        [
            ["Initiative", initiative.Name],
            ["Business unit", initiative.BusinessUnit?.Name],
            ["Participating business units", string.Join("; ", initiative.ParticipatingBusinessUnits.Select(p => p.BusinessUnit?.Name).Prepend(initiative.BusinessUnit?.Name).Where(n => n is not null).Distinct())],
            ["Status", initiative.Status.ToString()],
            ["Sizing", initiative.SizingMethod == Domain.Enums.SizingMethod.Direct ? "Direct" : $"{initiative.SizingMethod} {initiative.SizeKey}"],
            ["Planning mode", initiative.PlanningMode.ToString()],
            ["Target start", initiative.TargetStart],
            ["Target end", initiative.TargetEnd],
            ["Forecast hours", forecast.TotalHours],
            ["Forecast labor cost", forecast.LaborCost],
            ["Forecast non-labor cost", forecast.NonLaborCost],
            ["Forecast cost", forecast.TotalCost],
            ["Contingency %", forecast.ContingencyPct],
            ["Contingency cost", forecast.ContingencyCost],
            ["Forecast cost with contingency", forecast.TotalCostWithContingency],
            ["Forecast capex", phasing.ForecastCapexCost],
            ["Forecast opex", phasing.ForecastOpexCost],
            ["Estimate confidence", initiative.EstimateConfidence?.ToString()],
            ["Forecast complete", forecast.IsComplete],
            ["Baseline version", baseline?.Version],
            ["Baseline hours", variance.BaselineHours],
            ["Baseline cost", variance.BaselineCost],
            ["Actual hours", variance.ActualHours],
            ["Actual cost", variance.ActualCost],
            ["Cost variance", variance.CostVariance],
            ["Cost variance %", variance.CostVariancePct],
            ["ETC as of", variance.AsOf],
            ["ETC hours", variance.EtcHours],
            ["ETC cost", variance.EtcCost],
            ["EAC hours", variance.EacHours],
            ["EAC cost", variance.EacCost],
            ["Projected variance", variance.EacCostVariance],
            ["Projected variance %", variance.EacCostVariancePct],
            ["Approved budget", budget.Budget],
            ["Budget fiscal year", budget.FiscalYear],
            ["Budget compared against", budget.HasBudget ? (budget.UsesEac ? "EAC" : "Forecast with contingency") : null],
            ["Budget remaining", budget.Remaining],
            ["Budget used %", budget.UtilizationPct],
            ["Over budget", budget.HasBudget ? budget.OverBudget : null],
            ["Threshold %", variance.ThresholdPct],
            ["Over threshold", variance.ExceedsThreshold],
            ["Unpriced actual rows", variance.UnpricedEntries],
            ["Pending change requests", initiative.ChangeRequests.Count(c => c.Status == Domain.Enums.ChangeRequestStatus.Pending)],
            ["Approved change requests", initiative.ChangeRequests.Count(c => c.Status == Domain.Enums.ChangeRequestStatus.Approved)],
            ["Implemented change requests", initiative.ChangeRequests.Count(c => c.Status == Domain.Enums.ChangeRequestStatus.Implemented)]
        ]);

        var changeRequests = new ExportTable("Change requests", ChangeRequestExport.Headers,
            initiative.ChangeRequests.OrderBy(c => c.Number).Select(ChangeRequestExport.Row).ToList());

        var forecastLines = new ExportTable("Forecast",
            ["Phase", "Business unit", "Resource type", "Seniority", "Location", "Class", "Vendor", "People", "Unassigned seats", "Quantity", "Hours each", "Hours", "Hourly rate", "Cost", "Capex / Opex", "Contract", "Cost center"],
            forecast.Lines.Select(l => (IReadOnlyList<object?>)
            [
                phases.GetValueOrDefault(l.Allocation.PhaseId), l.Allocation.BusinessUnit?.Name, resourceTypeNames.GetValueOrDefault(l.Allocation.ResourceTypeId),
                seniorityNames.GetValueOrDefault(l.Allocation.SeniorityId), l.Allocation.Location, l.Allocation.ResourcingClass.ToString(), l.Allocation.Vendor?.Name,
                l.Allocation.PersonNames, l.Allocation.UnassignedSeats, l.Allocation.Quantity, l.Allocation.EstimatedHours, l.Hours, l.HourlyRate, l.IsUnpriced ? null : l.Cost,
                l.Allocation.Capitalization.ToString(), l.Allocation.ContractReference, l.Allocation.CostCenter
            ]).ToList());

        var nonLaborLines = new ExportTable("Non-labor forecast",
            ["Phase", "Category", "Description", "Billing", "Quantity", "Unit cost", "Start", "End", "Periods", "Cost", "Capex / Opex", "Contract", "Cost center"],
            forecast.NonLaborLines.Select(l => (IReadOnlyList<object?>)
            [
                l.Line.PhaseId is { } pid ? phases.GetValueOrDefault(pid) : VarianceCalculator.WholeInitiative,
                VarianceCalculator.CategoryLabel(l.Line.Category), l.Line.Description, l.Line.BillingModel.ToString(),
                l.Line.Quantity, l.Line.UnitCost, l.Start, l.End, l.Periods, l.Cost, l.Line.Capitalization.ToString(), l.Line.ContractReference, l.Line.CostCenter
            ]).ToList());

        var baselineLines = new ExportTable("Baseline",
            ["Version", "Phase", "Business unit", "Resource type", "Seniority", "Location", "Class", "Vendor", "Person", "Hours", "Hourly rate", "Cost", "Capex / Opex"],
            (baseline?.Lines ?? []).Select(l => (IReadOnlyList<object?>)
            [
                baseline!.Version, l.PhaseName, l.BusinessUnitName, l.ResourceTypeName,
                l.SeniorityName, l.Location, l.ResourcingClass.ToString(), l.VendorName, l.PersonName, l.Hours, l.HourlyRate, l.Cost, l.Capitalization.ToString()
            ]).ToList());

        var baselineNonLabor = new ExportTable("Baseline non-labor",
            ["Version", "Phase", "Category", "Description", "Billing", "Quantity", "Unit cost", "Start", "End", "Periods", "Cost", "Capex / Opex"],
            (baseline?.NonLaborLines ?? []).Select(l => (IReadOnlyList<object?>)
            [
                baseline!.Version, l.PhaseId is { } pid ? l.PhaseName ?? $"Phase #{pid}" : VarianceCalculator.WholeInitiative,
                VarianceCalculator.CategoryLabel(l.Category), l.Description, l.BillingModel.ToString(),
                l.Quantity, l.UnitCost, l.StartDate, l.EndDate, l.Periods, l.Cost, l.Capitalization.ToString()
            ]).ToList());

        var variancePhase = VarianceTable("Variance by phase", variance.ByPhase);
        var varianceType = VarianceTable("Variance by resource type", variance.ByResourceType);
        var variancePerson = VarianceTable("Variance by person", variance.ByPerson);
        var varianceCategory = VarianceTable("Variance by category", variance.ByCategory);

        var actuals = new ExportTable("Actuals",
            ["Work date", "Person", "External person id", "External project id", "Hours", "Sourced cost", "Calculated cost", "Effective cost", "Source", "Reference", "Unmapped"],
            entries.Select(e => (IReadOnlyList<object?>)
            [
                e.WorkDate, e.Person?.DisplayName, e.ExternalPersonId, e.ExternalProjectId, e.Hours, e.SourcedCost, e.CalculatedCost, e.EffectiveCost,
                e.ActualsImport?.Source, e.SourceReference, e.IsUnmapped
            ]).ToList());

        var adjustmentTable = new ExportTable("Adjustments",
            ["Created", "Created by", "Category", "Hours", "Cost", "Reason"],
            adjustments.Select(a => (IReadOnlyList<object?>)[a.CreatedAt, a.CreatedBy, VarianceCalculator.CategoryLabel(a.Category), a.Hours, a.Cost, a.Reason]).ToList());

        return [summary, forecastLines, nonLaborLines, MonthlyPhasingExport.Table("By month", phasing), MonthlyPhasingExport.FiscalTable("By fiscal period", phasing, fiscal), baselineLines, baselineNonLabor, variancePhase, varianceType, variancePerson, varianceCategory, actuals, adjustmentTable, changeRequests];
    }

    private static ExportTable VarianceTable(string name, IReadOnlyList<VarianceRow> rows) =>
        new(name,
            ["Group", "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Hours variance", "Cost variance", "Cost variance %", "ETC hours", "ETC cost", "EAC hours", "EAC cost", "Projected variance", "Projected variance %"],
            rows.Select(r => (IReadOnlyList<object?>)[r.Label, r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.HoursVariance, r.CostVariance, r.CostVariancePct, r.EtcHours, r.EtcCost, r.EacHours, r.EacCost, r.EacCostVariance, r.EacCostVariancePct]).ToList());
}

public static class ChangeRequestExport
{
    public static readonly IReadOnlyList<string> Headers =
        ["Code", "Type", "Status", "Title", "Description", "Reason", "Requested by", "Requested at",
         "Estimated cost impact", "Estimated hours impact", "Proposed target end",
         "Baseline version before", "Forecast hours before", "Forecast cost before", "Target end before",
         "Forecast hours after", "Forecast cost after", "Target end after", "Actual cost impact", "Actual hours impact",
         "Decided by", "Decided at", "Decision note", "Resulting baseline version"];

    public static IReadOnlyList<object?> Row(ChangeRequest c) =>
        [c.Code, c.Type.ToString(), c.Status.ToString(), c.Title, c.Description, c.Reason, c.RequestedBy, c.RequestedAt,
         c.EstimatedCostImpact, c.EstimatedHoursImpact, c.ProposedTargetEnd,
         c.BaselineVersionBefore, c.ForecastHoursBefore, c.ForecastCostBefore, c.TargetEndBefore,
         c.ForecastHoursAfter, c.ForecastCostAfter, c.TargetEndAfter, c.ActualCostImpact, c.ActualHoursImpact,
         c.DecidedBy, c.DecidedAt, c.DecisionNote, c.ResultingBaseline?.Version];
}

public static class CapacityExport
{
    public static IReadOnlyList<ExportTable> Build(CapacityHeatmap heatmap, bool byPerson = false)
    {
        IReadOnlyList<string> head = byPerson ? ["Person", "Resource type"] : ["Resource type"];
        IReadOnlyList<object?> Lead(CapacityRow r) => byPerson ? [r.IsUnassigned ? "(unassigned)" : r.PersonName, r.ResourceTypeName] : [r.ResourceTypeName];

        var cells = new ExportTable("Capacity",
            [.. head, "Month", "Demand hours", "Demand FTE", "Headcount", "Supply hours", "Utilization", "Over-allocated"],
            heatmap.Rows.SelectMany(r => r.Cells.Select(c => (IReadOnlyList<object?>)
                [.. Lead(r), c.Month, c.DemandHours, c.DemandFte, c.Headcount, c.SupplyHours, c.Utilization, !r.IsUnassigned && c.IsOverAllocated])).ToList());

        var contributions = new ExportTable("Capacity by initiative",
            [.. head, "Month", "Id", "Initiative", "Hours"],
            heatmap.Rows.SelectMany(r => r.Cells.SelectMany(c => c.Contributions.Select(x => (IReadOnlyList<object?>)
                [.. Lead(r), c.Month, x.Initiative.Id, x.Initiative.Name, x.Hours]))).ToList());

        return [cells, contributions];
    }
}
