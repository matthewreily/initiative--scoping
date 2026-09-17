using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Services;

/// <summary>Copied plan (phases, allocations, non-labor lines); allocations and lines reference the new phases by navigation, not id.</summary>
public sealed record PlanCopy(List<Phase> Phases, List<InitiativeAllocation> Allocations, List<InitiativeNonLaborCost> NonLaborCosts);

/// <summary>
/// Scenarios are Draft initiatives that hang off a parent initiative (<see cref="Initiative.ScenarioOfId"/>). They share the parent's
/// sponsor BU and members, carry their own copy of the plan, never appear on the Portfolio or capacity views and cannot be activated;
/// the only way a scenario reaches production is by being promoted, which replaces the parent's plan with the scenario's.
/// </summary>
public static class ScenarioPlanner
{
    /// <summary>Creates an unsaved Draft scenario of <paramref name="source"/> (or of its parent when <paramref name="source"/> is itself a scenario).</summary>
    public static Initiative Clone(Initiative source, string name, string createdBy, DateTimeOffset now)
    {
        var plan = CopyPlan(source);
        var scenario = new Initiative
        {
            Name = name,
            Description = source.Description,
            BusinessUnitId = source.BusinessUnitId,
            SponsoringTeam = source.SponsoringTeam,
            Status = InitiativeStatus.Draft,
            ScenarioOfId = source.ScenarioOfId ?? source.Id,
            CreatedBy = createdBy,
            CreatedAt = now,
            ParticipatingBusinessUnits = source.ParticipatingBusinessUnits
                .Select(p => new InitiativeBusinessUnit { BusinessUnitId = p.BusinessUnitId }).ToList(),
            Members = source.Members.Select(m => new InitiativeMember { UserId = m.UserId, Role = m.Role }).ToList(),
            Phases = plan.Phases,
            Allocations = plan.Allocations,
            NonLaborCosts = plan.NonLaborCosts
        };
        CopyPlanningFields(source, scenario);
        return scenario;
    }

    /// <summary>
    /// Replaces <paramref name="target"/>'s plan with a copy of <paramref name="scenario"/>'s. The caller removes the target's previous
    /// phases/allocations/non-labor lines from the store; baselines, actuals, members and source mappings are untouched.
    /// </summary>
    public static PlanCopy Promote(Initiative scenario, Initiative target)
    {
        var plan = CopyPlan(scenario);
        CopyPlanningFields(scenario, target);
        target.Description = scenario.Description;
        target.Phases = plan.Phases;
        target.Allocations = plan.Allocations;
        target.NonLaborCosts = plan.NonLaborCosts;
        return plan;
    }

    public static PlanCopy CopyPlan(Initiative source)
    {
        var phases = new Dictionary<Phase, Phase>(ReferenceEqualityComparer.Instance);
        foreach (var p in source.Phases.OrderBy(p => p.Sequence).ThenBy(p => p.PlannedStart))
        {
            phases[p] = new Phase { Name = p.Name, Sequence = p.Sequence, PlannedStart = p.PlannedStart, PlannedEnd = p.PlannedEnd };
        }

        var byId = source.Phases.Where(p => p.Id != 0).ToDictionary(p => p.Id, p => phases[p]);

        Phase? Map(Phase? navigation, int? phaseId) =>
            navigation is not null ? phases[navigation]
            : phaseId is { } id && byId.TryGetValue(id, out var mapped) ? mapped
            : null;

        var allocations = source.Allocations.Select(a => new InitiativeAllocation
        {
            Phase = Map(a.Phase, a.PhaseId) ?? throw new InvalidOperationException($"Allocation {a.Id} references a phase outside the plan."),
            BusinessUnitId = a.BusinessUnitId,
            ResourceTypeId = a.ResourceTypeId,
            SeniorityId = a.SeniorityId,
            Location = a.Location,
            ResourcingClass = a.ResourcingClass,
            VendorId = a.VendorId,
            Quantity = a.Quantity,
            AllocationPercent = a.AllocationPercent,
            EstimatedHours = a.EstimatedHours,
            ContractReference = a.ContractReference,
            CostCenter = a.CostCenter
        }).ToList();

        var nonLabor = source.NonLaborCosts.Select(c => new InitiativeNonLaborCost
        {
            Phase = Map(c.Phase, c.PhaseId),
            Category = c.Category,
            CostCatalogItemId = c.CostCatalogItemId,
            Description = c.Description,
            BillingModel = c.BillingModel,
            Quantity = c.Quantity,
            UnitCost = c.UnitCost,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            ContractReference = c.ContractReference,
            CostCenter = c.CostCenter
        }).ToList();

        return new PlanCopy(phases.Values.ToList(), allocations, nonLabor);
    }

    private static void CopyPlanningFields(Initiative from, Initiative to)
    {
        to.SizingMethod = from.SizingMethod;
        to.SizeKey = from.SizeKey;
        to.PlanningMode = from.PlanningMode;
        to.TargetStart = from.TargetStart;
        to.TargetEnd = from.TargetEnd;
        to.VarianceThresholdPct = from.VarianceThresholdPct;
        to.ContingencyPct = from.ContingencyPct;
        to.EstimateConfidence = from.EstimateConfidence;
    }
}

/// <summary>One column of the side-by-side comparison: the parent plan first, then each scenario.</summary>
public sealed record ScenarioColumn(Initiative Initiative, ForecastResult Forecast, bool IsParent)
{
    public decimal Hours => Forecast.TotalHours;
    public decimal LaborCost => Forecast.LaborCost;
    public decimal NonLaborCost => Forecast.NonLaborCost;
    public decimal TotalCost => Forecast.TotalCost;
    public decimal ContingencyCost => Forecast.ContingencyCost;
    public decimal TotalCostWithContingency => Forecast.TotalCostWithContingency;
    public decimal InternalHours => Forecast.Lines.Where(l => l.Allocation.ResourcingClass == ResourcingClass.InternalFte).Sum(l => l.Hours);
    public decimal VendorHours => Forecast.Lines.Where(l => l.Allocation.ResourcingClass == ResourcingClass.Vendor).Sum(l => l.Hours);
    public decimal VendorCost => Forecast.Lines.Where(l => l.Allocation.ResourcingClass == ResourcingClass.Vendor).Sum(l => l.Cost);
    public int HeadCount => Initiative.Allocations.Sum(a => a.Quantity);
    public int UnpricedLines => Forecast.Lines.Count(l => l.IsUnpriced);
    public DateOnly? PlanStart => Initiative.Phases.Count == 0 ? null : Initiative.Phases.Min(p => p.PlannedStart);
    public DateOnly? PlanEnd => Initiative.Phases.Count == 0 ? null : Initiative.Phases.Max(p => p.PlannedEnd);

    public decimal HoursByResourceType(int resourceTypeId) =>
        Forecast.Lines.Where(l => l.Allocation.ResourceTypeId == resourceTypeId).Sum(l => l.Hours);
}

public sealed record ScenarioComparison(IReadOnlyList<ScenarioColumn> Columns, IReadOnlyList<int> ResourceTypeIds)
{
    public ScenarioColumn Parent => Columns[0];
    public IEnumerable<ScenarioColumn> Scenarios => Columns.Skip(1);

    public static ScenarioComparison Build(Initiative parent, IEnumerable<Initiative> scenarios, IReadOnlyList<RateCard> cards)
    {
        var columns = new List<ScenarioColumn> { new(parent, ForecastCalculator.Calculate(parent, cards), true) };
        columns.AddRange(scenarios.OrderBy(s => s.Id).Select(s => new ScenarioColumn(s, ForecastCalculator.Calculate(s, cards), false)));
        var types = columns.SelectMany(c => c.Initiative.Allocations.Select(a => a.ResourceTypeId)).Distinct().OrderBy(t => t).ToList();
        return new ScenarioComparison(columns, types);
    }
}
