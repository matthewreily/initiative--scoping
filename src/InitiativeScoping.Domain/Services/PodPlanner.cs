using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record PodPlan(IReadOnlyList<InitiativeAllocation> Add, IReadOnlyList<InitiativeAllocation> Remove, IReadOnlyList<string> Skipped);

public static class PodPlanner
{
    public static PodPlan Assign(int initiativeId, Pod pod, IEnumerable<Phase> phases, decimal percent,
        IReadOnlyList<RateCard> cards, IReadOnlySet<DateOnly> holidays, decimal hoursPerDay)
    {
        if (percent <= 0 || percent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(percent));
        }

        var add = new List<InitiativeAllocation>();
        var skipped = new List<string>();
        foreach (var phase in phases.DistinctBy(p => p.Id))
        {
            foreach (var person in pod.Members.Select(m => m.Person).OfType<Person>().Where(p => p.IsActive).DistinctBy(p => p.Id))
            {
                var key = new RateKey(person.ResourceTypeId, person.SeniorityId, person.Location, person.ResourcingClassId, person.VendorId);
                if (RateResolver.ResolveBlended(cards, key, phase.PlannedStart, phase.PlannedEnd) is null)
                {
                    skipped.Add($"{person.DisplayName} ({phase.Name}): no priced rate");
                    continue;
                }

                add.Add(new InitiativeAllocation
                {
                    InitiativeId = initiativeId, PodId = pod.Id, PhaseId = phase.Id, BusinessUnitId = pod.BusinessUnitId,
                    ResourceTypeId = person.ResourceTypeId, SeniorityId = person.SeniorityId, Location = person.Location,
                    ResourcingClassId = person.ResourcingClassId, VendorId = person.VendorId, Quantity = 1,
                    People = [new InitiativeAllocationPerson { PersonId = person.Id }], AllocationPercent = percent,
                    EstimatedHours = DurationCalculator.Hours(percent, DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, holidays), hoursPerDay),
                    CapexPercent = person.ResourcingClass?.DefaultCapexPercent ?? 0
                });
            }
        }

        return new PodPlan(add, [], skipped);
    }

    public static PodPlan Resync(int initiativeId, Pod pod, IReadOnlyList<Phase> phases, IEnumerable<InitiativeAllocation> allocations,
        IReadOnlyList<RateCard> cards, IReadOnlySet<DateOnly> holidays, decimal hoursPerDay)
    {
        var existing = allocations.Where(a => a.InitiativeId == initiativeId && a.PodId == pod.Id).OrderBy(a => a.Id).ToList();
        var members = pod.Members.Where(m => m.Person?.IsActive == true).Select(m => m.PersonId).ToHashSet();
        var remove = existing.Where(a => !a.PersonIds.Any(members.Contains)).ToList();
        var add = new List<InitiativeAllocation>();
        var skipped = new List<string>();
        foreach (var group in existing.GroupBy(a => a.PhaseId))
        {
            var phase = phases.Single(p => p.Id == group.Key);
            var first = group.First();
            var percent = first.AllocationPercent ?? DurationCalculator.PercentFor(first.EstimatedHours,
                DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, holidays), hoursPerDay);
            if (percent <= 0 || percent > 100)
            {
                skipped.Add($"{phase.Name}: existing allocation % is outside 0–100; assign new members manually");
                continue;
            }

            var present = group.SelectMany(a => a.PersonIds).ToHashSet();
            var joiners = new Pod { Id = pod.Id, Name = pod.Name, BusinessUnitId = pod.BusinessUnitId, Members = pod.Members.Where(m => !present.Contains(m.PersonId)).ToList() };
            var plan = Assign(initiativeId, joiners, [phase], percent, cards, holidays, hoursPerDay);
            add.AddRange(plan.Add);
            skipped.AddRange(plan.Skipped);
        }

        return new PodPlan(add, remove, skipped);
    }
}
