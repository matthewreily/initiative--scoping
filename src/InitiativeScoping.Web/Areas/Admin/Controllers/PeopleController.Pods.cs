using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.People;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public partial class PeopleController
{
    private sealed record PeoplePodImportResult(List<string> CreatedPods, List<string> MembershipsAdded,
        List<string> MembershipsUpdated, List<string> InactivePods, List<string> Warnings)
    {
        public string Message => List("Pods created", CreatedPods) + List("Pod memberships added", MembershipsAdded)
            + List("Pod memberships updated", MembershipsUpdated) + List("Warning: inactive pods used (not reactivated)", InactivePods)
            + List("Membership warnings", Warnings);

        private static string List(string label, IReadOnlyList<string> items) => items.Count == 0 ? string.Empty
            : $" {label} ({items.Count}): {string.Join("; ", items.Take(5))}{(items.Count > 5 ? $" (+{items.Count - 5} more)" : "")}.";
    }

    private async Task<PeoplePodImportResult> ImportPods(IReadOnlyDictionary<PeopleCsvRow, Person> people, CancellationToken ct)
    {
        var result = new PeoplePodImportResult([], [], [], [], []);
        var pods = await db.Pods.Include(p => p.Members).ToDictionaryAsync(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase, ct);
        var created = new List<Pod>();
        var requests = new Dictionary<Pod, List<(PeopleCsvPod Entry, Person Person)>>();
        foreach (var (row, person) in people)
        {
            foreach (var entry in row.Pods ?? [])
            {
                if (!pods.TryGetValue(entry.Name, out var pod))
                {
                    pod = new Pod { Name = entry.Name, BusinessUnitId = person.BusinessUnitId, IsActive = true };
                    db.Pods.Add(pod);
                    pods.Add(entry.Name, pod);
                    created.Add(pod);
                }
                if (!requests.TryGetValue(pod, out var entries)) requests[pod] = entries = [];
                entries.Add((entry, person));
            }
        }
        if (created.Count > 0) await db.SaveChangesAsync(ct);
        foreach (var pod in created)
        {
            result.CreatedPods.Add(pod.Name);
            audit.Record(nameof(Pod), pod.Id, AuditActions.Create, new { pod.Name, pod.BusinessUnitId, pod.IsActive });
        }
        foreach (var (pod, entries) in requests)
        {
            var before = pod.Members.Select(m => new { m.PersonId, m.MembershipPercent }).ToArray();
            var changed = false;
            if (!pod.IsActive) result.InactivePods.Add(pod.Name);
            foreach (var (entry, person) in entries)
            {
                var membership = pod.Members.SingleOrDefault(m => m.PersonId == person.Id);
                if (membership is null)
                {
                    var percent = entry.MembershipPercent ?? 100m;
                    pod.Members.Add(new PodMember { PersonId = person.Id, MembershipPercent = percent });
                    result.MembershipsAdded.Add($"{person.DisplayName} → {pod.Name}: {percent:0.##}%");
                    changed = true;
                }
                else if (entry.MembershipPercent is { } percent && membership.MembershipPercent != percent)
                {
                    result.MembershipsUpdated.Add($"{person.DisplayName} → {pod.Name}: {membership.MembershipPercent:0.##}% → {percent:0.##}%");
                    membership.MembershipPercent = percent;
                    changed = true;
                }
            }
            if (changed)
            {
                audit.Record(nameof(Pod), pod.Id, AuditActions.Update, new
                {
                    Operation = "ImportPeopleMemberships", Before = new { Memberships = before },
                    After = new { Memberships = pod.Members.Select(m => new { m.PersonId, m.MembershipPercent }).ToArray() }
                });
            }
        }
        var memberships = pods.Values.SelectMany(p => p.Members).ToLookup(m => m.PersonId);
        foreach (var person in people.Values)
        {
            var total = memberships[person.Id].Sum(m => m.MembershipPercent);
            if (PodMembershipWarning.For(person.DisplayName, total) is { } warning) result.Warnings.Add(warning);
        }
        return result;
    }
}
