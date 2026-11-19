using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class PodsController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.Pods.Include(x => x.BusinessUnit).Include(x => x.Members).ThenInclude(m => m.Person).Include(x => x.Applications).OrderBy(x => x.Name).AsSplitQuery().ToListAsync(ct));
    public async Task<IActionResult> Create(CancellationToken ct) => View("Edit", await Populate(new PodEditModel(), ct));
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var pod = await Load(id, ct);
        return pod is null ? NotFound() : View(await Populate(new PodEditModel
        {
            Id = id, Name = pod.Name, IsActive = pod.IsActive, BusinessUnitId = pod.BusinessUnitId, Description = pod.Description,
            PersonIds = pod.Members.Select(x => x.PersonId).ToArray(), MembershipPercents = pod.Members.ToDictionary(x => x.PersonId, x => x.MembershipPercent), ApplicationIds = pod.Applications.Select(x => x.ApplicationPlatformId).ToArray()
        }, ct));
    }
    [HttpPost]
    public Task<IActionResult> Create(PodEditModel model, CancellationToken ct) => Save(0, model, ct);
    [HttpPost]
    public Task<IActionResult> Edit(int id, PodEditModel model, CancellationToken ct) => Save(id, model, ct);

    private async Task<IActionResult> Save(int id, PodEditModel model, CancellationToken ct)
    {
        model.Name ??= "";
        model.Id = id;
        var pod = id == 0 ? new Pod { Name = "" } : await Load(id, ct);
        if (pod is null) return NotFound();
        if (model.Name.IndexOfAny([';', ':']) >= 0)
            ModelState.AddModelError(nameof(model.Name), "Pod names cannot contain ';' or ':' because these characters separate pod memberships in People CSV files.");
        if (await db.Pods.AnyAsync(x => x.Id != id && x.Name.ToLower() == model.Name.Trim().ToLower(), ct))
            ModelState.AddModelError(nameof(model.Name), "A pod with this name already exists.");
        if (!await db.BusinessUnits.AnyAsync(x => x.Id == model.BusinessUnitId && (x.IsActive || x.Id == pod.BusinessUnitId), ct))
            ModelState.AddModelError(nameof(model.BusinessUnitId), "Choose an active business unit.");
        var people = model.PersonIds.Distinct().ToArray();
        foreach (var personId in people)
        {
            var percent = model.MembershipPercents.GetValueOrDefault(personId, 100m);
            if (percent < 1 || percent > 100)
                ModelState.AddModelError($"MembershipPercents[{personId}]", "Membership % must be between 1 and 100.");
        }
        var apps = model.ApplicationIds.Distinct().ToArray();
        if (await db.People.CountAsync(x => people.Contains(x.Id) && x.IsActive, ct) != people.Length)
            ModelState.AddModelError(nameof(model.PersonIds), "Choose active roster people only.");
        if (await db.ApplicationPlatforms.CountAsync(x => apps.Contains(x.Id) && x.IsActive, ct) != apps.Length)
            ModelState.AddModelError(nameof(model.ApplicationIds), "Choose active applications/platforms only.");
        if (!ModelState.IsValid) return View("Edit", await Populate(model, ct));
        var before = Snapshot(pod);
        pod.Name = model.Name.Trim(); pod.IsActive = model.IsActive; pod.BusinessUnitId = model.BusinessUnitId; pod.Description = model.Description?.Trim();
        pod.Members.RemoveAll(x => !people.Contains(x.PersonId));
        foreach (var personId in people.Except(pod.Members.Select(x => x.PersonId))) pod.Members.Add(new PodMember { PersonId = personId });
        foreach (var member in pod.Members) member.MembershipPercent = model.MembershipPercents.GetValueOrDefault(member.PersonId, 100m);
        pod.Applications.RemoveAll(x => !apps.Contains(x.ApplicationPlatformId));
        foreach (var appId in apps.Except(pod.Applications.Select(x => x.ApplicationPlatformId))) pod.Applications.Add(new PodApplication { ApplicationPlatformId = appId });
        if (id == 0) db.Pods.Add(pod);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Pod), pod.Id, id == 0 ? AuditActions.Create : AuditActions.Update, new { Before = before, After = Snapshot(pod) });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Pod saved.");
    }
    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var pod = await Load(id, ct);
        if (pod is null) return NotFound();
        if (await db.InitiativeAllocations.AnyAsync(x => x.PodId == id, ct)) return RedirectWithError("Pod is referenced by allocations; deactivate it instead.");
        db.Pods.Remove(pod);
        audit.Record(nameof(Pod), id, AuditActions.Delete, Snapshot(pod));
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Pod deleted.");
    }
    private Task<Pod?> Load(int id, CancellationToken ct) => db.Pods.Include(x => x.Members).Include(x => x.Applications).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct);
    private async Task<PodEditModel> Populate(PodEditModel model, CancellationToken ct)
    {
        model.BusinessUnits = await db.BusinessUnits.Where(x => x.IsActive || x.Id == model.BusinessUnitId).OrderBy(x => x.Name).ToListAsync(ct);
        model.People = await db.People.Where(x => x.IsActive).OrderBy(x => x.DisplayName).ToListAsync(ct);
        model.Applications = await db.ApplicationPlatforms.Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct);
        var otherMemberships = await db.PodMembers.Where(m => m.PodId != model.Id && model.PersonIds.Contains(m.PersonId)).Select(m => new { m.PersonId, m.MembershipPercent }).ToListAsync(ct);
        model.MembershipWarnings = model.People.Where(p => model.PersonIds.Contains(p.Id))
            .Select(p => new { p.DisplayName, Total = otherMemberships.Where(m => m.PersonId == p.Id).Sum(m => m.MembershipPercent) + model.MembershipPercents.GetValueOrDefault(p.Id, 100m) })
            .Select(p => PodMembershipWarning.For(p.DisplayName, p.Total)).OfType<string>().ToList();
        return model;
    }
    private static object Snapshot(Pod p) => new { p.Name, p.IsActive, p.BusinessUnitId, p.Description, PersonIds = p.Members.Select(x => x.PersonId).ToArray(), Memberships = p.Members.Select(x => new { x.PersonId, x.MembershipPercent }).ToArray(), ApplicationIds = p.Applications.Select(x => x.ApplicationPlatformId).ToArray() };
}
