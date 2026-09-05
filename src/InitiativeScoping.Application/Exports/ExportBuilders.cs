using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Application.Exports;

public static class PortfolioExport
{
    public static IReadOnlyList<ExportTable> Build(PortfolioResult portfolio)
    {
        var initiatives = new ExportTable("Initiatives",
            ["Id", "Initiative", "Business unit", "Status", "Target start", "Baseline version",
             "Forecast hours", "Forecast cost", "Internal forecast cost", "Vendor forecast cost", "Non-labor forecast cost",
             "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Cost variance", "Cost variance %",
             "ETC cost", "EAC cost", "Projected variance", "Projected variance %",
             "Threshold %", "Over threshold", "Unpriced forecast", "Unpriced actuals", "Planning mode", "Target end"],
            portfolio.Rows.Select(r => (IReadOnlyList<object?>)
            [
                r.Initiative.Id, r.Initiative.Name, r.Initiative.BusinessUnit?.Name, r.Initiative.Status.ToString(), r.Initiative.TargetStart, r.BaselineVersion,
                r.ForecastHours, r.ForecastCost, r.InternalForecastCost, r.VendorForecastCost, r.NonLaborForecastCost,
                r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.CostVariance, r.CostVariancePct,
                r.Variance.EtcCost, r.Variance.EacCost, r.Variance.EacCostVariance, r.Variance.EacCostVariancePct,
                r.Variance.ThresholdPct, r.ExceedsThreshold, r.HasUnpricedForecast, r.HasUnpricedActuals,
                r.Initiative.PlanningMode.ToString(), r.Initiative.TargetEnd
            ]).ToList());

        return
        [
            initiatives,
            Groups("By business unit", portfolio.ByBusinessUnit),
            Groups("By status", portfolio.ByStatus)
        ];
    }

    private static ExportTable Groups(string name, IReadOnlyList<PortfolioGroup> groups) =>
        new(name,
            ["Group", "Initiatives", "Forecast cost", "Baseline cost", "Actual cost", "Cost variance", "Cost variance %", "Over threshold"],
            groups.Select(g => (IReadOnlyList<object?>)[g.Label, g.Count, g.ForecastCost, g.BaselineCost, g.ActualCost, g.CostVariance, g.CostVariancePct, g.OverThreshold]).ToList());
}

public static class InitiativeExport
{
    public static IReadOnlyList<ExportTable> Build(
        Initiative initiative,
        ForecastResult forecast,
        VarianceResult variance,
        IReadOnlyList<ActualEntry> entries,
        IReadOnlyList<ActualAdjustment> adjustments,
        IReadOnlyDictionary<int, string> resourceTypeNames)
    {
        var phases = initiative.Phases.ToDictionary(p => p.Id, p => p.Name);
        var baseline = variance.Baseline;

        var summary = new ExportTable("Summary", ["Field", "Value"],
        [
            ["Initiative", initiative.Name],
            ["Business unit", initiative.BusinessUnit?.Name],
            ["Status", initiative.Status.ToString()],
            ["Sizing", initiative.SizingMethod == Domain.Enums.SizingMethod.Direct ? "Direct" : $"{initiative.SizingMethod} {initiative.SizeKey}"],
            ["Planning mode", initiative.PlanningMode.ToString()],
            ["Target start", initiative.TargetStart],
            ["Target end", initiative.TargetEnd],
            ["Forecast hours", forecast.TotalHours],
            ["Forecast labor cost", forecast.LaborCost],
            ["Forecast non-labor cost", forecast.NonLaborCost],
            ["Forecast cost", forecast.TotalCost],
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
            ["Threshold %", variance.ThresholdPct],
            ["Over threshold", variance.ExceedsThreshold],
            ["Unpriced actual rows", variance.UnpricedEntries]
        ]);

        var forecastLines = new ExportTable("Forecast",
            ["Phase", "Resource type", "Seniority", "Location", "Class", "Quantity", "Hours each", "Hours", "Hourly rate", "Cost", "Contract", "Cost center"],
            forecast.Lines.Select(l => (IReadOnlyList<object?>)
            [
                phases.GetValueOrDefault(l.Allocation.PhaseId), resourceTypeNames.GetValueOrDefault(l.Allocation.ResourceTypeId),
                l.Allocation.Seniority.ToString(), l.Allocation.Location, l.Allocation.ResourcingClass.ToString(),
                l.Allocation.Quantity, l.Allocation.EstimatedHours, l.Hours, l.HourlyRate, l.IsUnpriced ? null : l.Cost,
                l.Allocation.ContractReference, l.Allocation.CostCenter
            ]).ToList());

        var nonLaborLines = new ExportTable("Non-labor forecast",
            ["Phase", "Category", "Description", "Billing", "Quantity", "Unit cost", "Start", "End", "Periods", "Cost", "Contract", "Cost center"],
            forecast.NonLaborLines.Select(l => (IReadOnlyList<object?>)
            [
                l.Line.PhaseId is { } pid ? phases.GetValueOrDefault(pid) : VarianceCalculator.WholeInitiative,
                VarianceCalculator.CategoryLabel(l.Line.Category), l.Line.Description, l.Line.BillingModel.ToString(),
                l.Line.Quantity, l.Line.UnitCost, l.Start, l.End, l.Periods, l.Cost, l.Line.ContractReference, l.Line.CostCenter
            ]).ToList());

        var baselineLines = new ExportTable("Baseline",
            ["Version", "Phase", "Resource type", "Seniority", "Location", "Class", "Hours", "Hourly rate", "Cost"],
            (baseline?.Lines ?? []).Select(l => (IReadOnlyList<object?>)
            [
                baseline!.Version, phases.GetValueOrDefault(l.PhaseId), resourceTypeNames.GetValueOrDefault(l.ResourceTypeId),
                l.Seniority.ToString(), l.Location, l.ResourcingClass.ToString(), l.Hours, l.HourlyRate, l.Cost
            ]).ToList());

        var baselineNonLabor = new ExportTable("Baseline non-labor",
            ["Version", "Phase", "Category", "Description", "Billing", "Quantity", "Unit cost", "Start", "End", "Periods", "Cost"],
            (baseline?.NonLaborLines ?? []).Select(l => (IReadOnlyList<object?>)
            [
                baseline!.Version, l.PhaseId is { } pid ? phases.GetValueOrDefault(pid) : VarianceCalculator.WholeInitiative,
                VarianceCalculator.CategoryLabel(l.Category), l.Description, l.BillingModel.ToString(),
                l.Quantity, l.UnitCost, l.StartDate, l.EndDate, l.Periods, l.Cost
            ]).ToList());

        var variancePhase = VarianceTable("Variance by phase", variance.ByPhase);
        var varianceType = VarianceTable("Variance by resource type", variance.ByResourceType);
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

        return [summary, forecastLines, nonLaborLines, baselineLines, baselineNonLabor, variancePhase, varianceType, varianceCategory, actuals, adjustmentTable];
    }

    private static ExportTable VarianceTable(string name, IReadOnlyList<VarianceRow> rows) =>
        new(name,
            ["Group", "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Hours variance", "Cost variance", "Cost variance %", "ETC hours", "ETC cost", "EAC hours", "EAC cost", "Projected variance", "Projected variance %"],
            rows.Select(r => (IReadOnlyList<object?>)[r.Label, r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.HoursVariance, r.CostVariance, r.CostVariancePct, r.EtcHours, r.EtcCost, r.EacHours, r.EacCost, r.EacCostVariance, r.EacCostVariancePct]).ToList());
}
