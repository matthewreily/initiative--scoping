using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Domain.Services;

public sealed record PodSeatRemoval(InitiativeAllocation Allocation, IReadOnlyList<InitiativeAllocationPerson> People);

public sealed record PodSeatChange(InitiativeAllocation Allocation, decimal PodPercent, decimal MembershipPercent,
    decimal AllocationPercent, decimal EstimatedHours, bool HandEdited)
{
    public void Apply()
    {
        var percent = Math.Round(AllocationPercent, 2, MidpointRounding.AwayFromZero);
        var hours = Math.Round(EstimatedHours, 2, MidpointRounding.AwayFromZero);
        Allocation.AllocationPercent = percent;
        Allocation.EstimatedHours = hours;
        Allocation.PodAllocationPercent = PodPercent;
        Allocation.PodMembershipPercent = MembershipPercent;
        Allocation.PodWrittenAllocationPercent = percent;
        Allocation.PodWrittenEstimatedHours = hours;
    }
}

public sealed record PodPlan(IReadOnlyList<InitiativeAllocation> Add, IReadOnlyList<InitiativeAllocation> Remove, IReadOnlyList<string> Skipped)
{
    public IReadOnlyList<PodSeatRemoval> Update { get; init; } = [];
    public IReadOnlyList<PodSeatChange> SeatsToUpdate { get; init; } = [];
}

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
            foreach (var member in pod.Members.Where(m => m.Person?.IsActive == true).DistinctBy(m => m.PersonId))
            {
                var person = member.Person!;
                if (member.MembershipPercent < 1 || member.MembershipPercent > 100)
                    throw new ArgumentOutOfRangeException(nameof(pod), "Membership % must be between 1 and 100.");
                var effectivePercent = Math.Round(percent * member.MembershipPercent / 100m, 2, MidpointRounding.AwayFromZero);
                var hours = DurationCalculator.Hours(effectivePercent, DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, holidays), hoursPerDay);
                if (person.ResourcingClass?.IsVendor == true && person.VendorId is null)
                {
                    skipped.Add($"{person.DisplayName} ({phase.Name}): select a vendor for vendor-supplied resources");
                    continue;
                }

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
                    People = [new InitiativeAllocationPerson { PersonId = person.Id }], AllocationPercent = effectivePercent,
                    EstimatedHours = hours, PodAllocationPercent = percent, PodMembershipPercent = member.MembershipPercent,
                    PodWrittenAllocationPercent = effectivePercent, PodWrittenEstimatedHours = hours,
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
        var update = existing.Where(a => a.PersonIds.Any(members.Contains))
            .Select(a => new PodSeatRemoval(a, a.People.Where(p => !members.Contains(p.PersonId)).ToList()))
            .Where(u => u.People.Count > 0).ToList();
        var add = new List<InitiativeAllocation>();
        var skipped = new List<string>();
        var changes = new List<PodSeatChange>();
        foreach (var group in existing.GroupBy(a => a.PhaseId))
        {
            var phase = phases.Single(p => p.Id == group.Key);
            var first = group.First();
            var days = DurationCalculator.WorkingDays(phase.PlannedStart, phase.PlannedEnd, holidays);
            var percent = first.PodAllocationPercent ?? first.AllocationPercent ?? DurationCalculator.PercentFor(first.EstimatedHours, days, hoursPerDay);
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
            foreach (var seat in group.Where(a => a.PersonIds.Any(members.Contains)))
            {
                var memberships = pod.Members.Where(m => m.Person?.IsActive == true && seat.PersonIds.Contains(m.PersonId)).Select(m => m.MembershipPercent).Distinct().ToList();
                if (memberships.All(p => p == (seat.PodMembershipPercent ?? 100m))) continue;
                if (memberships.Count != 1)
                {
                    skipped.Add($"{phase.Name}, allocation #{seat.Id}: members have different membership percentages; split this allocation into individual seats before re-syncing percentages");
                    continue;
                }
                var membership = memberships.Single();
                if (membership < 1 || membership > 100) throw new ArgumentOutOfRangeException(nameof(pod));
                var chosen = seat.PodAllocationPercent ?? percent;
                var effective = Math.Round(chosen * membership / 100m, 2, MidpointRounding.AwayFromZero);
                var handEdited = seat.PodWrittenAllocationPercent is null || seat.PodWrittenEstimatedHours is null
                    || seat.AllocationPercent != seat.PodWrittenAllocationPercent || seat.EstimatedHours != seat.PodWrittenEstimatedHours;
                changes.Add(new PodSeatChange(seat, chosen, membership, effective, DurationCalculator.Hours(effective, days, hoursPerDay), handEdited));
            }
        }

        return new PodPlan(add, remove, skipped) { Update = update, SeatsToUpdate = changes };
    }
}
