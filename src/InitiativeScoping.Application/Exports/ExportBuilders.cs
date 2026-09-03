using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Application.Exports;

public static class PortfolioExport
{
    public static IReadOnlyList<ExportTable> Build(PortfolioResult portfolio)
    {
        var initiatives = new ExportTable("Initiatives",
            ["Id", "Initiative", "Business unit", "Status", "Target start", "Baseline version",
             "Forecast hours", "Forecast cost", "Internal forecast cost", "Vendor forecast cost",
             "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Cost variance", "Cost variance %",
             "Threshold %", "Over threshold", "Unpriced forecast", "Unpriced actuals"],
            portfolio.Rows.Select(r => (IReadOnlyList<object?>)
            [
                r.Initiative.Id, r.Initiative.Name, r.Initiative.BusinessUnit?.Name, r.Initiative.Status.ToString(), r.Initiative.TargetStart, r.BaselineVersion,
                r.ForecastHours, r.ForecastCost, r.InternalForecastCost, r.VendorForecastCost,
                r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.CostVariance, r.CostVariancePct,
                r.Variance.ThresholdPct, r.ExceedsThreshold, r.HasUnpricedForecast, r.HasUnpricedActuals
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
            ["Target start", initiative.TargetStart],
            ["Forecast hours", forecast.TotalHours],
            ["Forecast cost", forecast.TotalCost],
            ["Forecast complete", forecast.IsComplete],
            ["Baseline version", baseline?.Version],
            ["Baseline hours", variance.BaselineHours],
            ["Baseline cost", variance.BaselineCost],
            ["Actual hours", variance.ActualHours],
            ["Actual cost", variance.ActualCost],
            ["Cost variance", variance.CostVariance],
            ["Cost variance %", variance.CostVariancePct],
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

        var baselineLines = new ExportTable("Baseline",
            ["Version", "Phase", "Resource type", "Seniority", "Location", "Class", "Hours", "Hourly rate", "Cost"],
            (baseline?.Lines ?? []).Select(l => (IReadOnlyList<object?>)
            [
                baseline!.Version, phases.GetValueOrDefault(l.PhaseId), resourceTypeNames.GetValueOrDefault(l.ResourceTypeId),
                l.Seniority.ToString(), l.Location, l.ResourcingClass.ToString(), l.Hours, l.HourlyRate, l.Cost
            ]).ToList());

        var variancePhase = VarianceTable("Variance by phase", variance.ByPhase);
        var varianceType = VarianceTable("Variance by resource type", variance.ByResourceType);

        var actuals = new ExportTable("Actuals",
            ["Work date", "Person", "External person id", "External project id", "Hours", "Sourced cost", "Calculated cost", "Effective cost", "Source", "Reference", "Unmapped"],
            entries.Select(e => (IReadOnlyList<object?>)
            [
                e.WorkDate, e.Person?.DisplayName, e.ExternalPersonId, e.ExternalProjectId, e.Hours, e.SourcedCost, e.CalculatedCost, e.EffectiveCost,
                e.ActualsImport?.Source, e.SourceReference, e.IsUnmapped
            ]).ToList());

        var adjustmentTable = new ExportTable("Adjustments",
            ["Created", "Created by", "Hours", "Cost", "Reason"],
            adjustments.Select(a => (IReadOnlyList<object?>)[a.CreatedAt, a.CreatedBy, a.Hours, a.Cost, a.Reason]).ToList());

        return [summary, forecastLines, baselineLines, variancePhase, varianceType, actuals, adjustmentTable];
    }

    private static ExportTable VarianceTable(string name, IReadOnlyList<VarianceRow> rows) =>
        new(name,
            ["Group", "Baseline hours", "Baseline cost", "Actual hours", "Actual cost", "Hours variance", "Cost variance", "Cost variance %"],
            rows.Select(r => (IReadOnlyList<object?>)[r.Label, r.BaselineHours, r.BaselineCost, r.ActualHours, r.ActualCost, r.HoursVariance, r.CostVariance, r.CostVariancePct]).ToList());
}
