using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class PodTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> Post(HttpClient client, string page, string action, params (string Key, string Value)[] fields)
    {
        var html = await client.GetStringAsync(page);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).Append(new("__RequestVerificationToken", WebUtility.HtmlDecode(token)))));
    }

    [Fact]
    public async Task Admin_catalogs_validate_names_and_audit_membership_and_app_changes()
    {
        var client = Client();
        foreach (var page in new[] { "/Admin/ApplicationPlatforms", "/Admin/Pods", "/Admin/Pods/Create" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(page)).StatusCode);
        var name = "App " + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Admin/ApplicationPlatforms/Create", "/Admin/ApplicationPlatforms/Create", ("Name", name), ("IsActive", "true"))).StatusCode);
        var duplicate = await Post(client, "/Admin/ApplicationPlatforms/Create", "/Admin/ApplicationPlatforms/Create", ("Name", name.ToLowerInvariant()));
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());
        Assert.Contains("required", await (await Post(client, "/Admin/ApplicationPlatforms/Create", "/Admin/ApplicationPlatforms/Create", ("Name", ""))).Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var app = await db.ApplicationPlatforms.SingleAsync(a => a.Name == name);
        var person = await db.People.FirstAsync(p => p.IsActive);
        var podName = "Pod " + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Admin/Pods/Create", "/Admin/Pods/Create", ("Name", podName), ("BusinessUnitId", person.BusinessUnitId.ToString()), ("IsActive", "true"), ("PersonIds", person.Id.ToString()), ("ApplicationIds", app.Id.ToString()))).StatusCode);
        var pod = await db.Pods.SingleAsync(p => p.Name == podName);
        Assert.True(await db.PodMembers.AnyAsync(m => m.PodId == pod.Id && m.PersonId == person.Id));
        Assert.True(await db.PodApplications.AnyAsync(a => a.PodId == pod.Id && a.ApplicationPlatformId == app.Id));
        Assert.Contains("PersonIds", (await db.AuditEvents.SingleAsync(a => a.Entity == nameof(Pod) && a.EntityId == pod.Id.ToString())).DiffJson);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/Admin/Pods/Edit/{pod.Id}", $"/Admin/Pods/Edit/{pod.Id}", ("Name", podName), ("BusinessUnitId", person.BusinessUnitId.ToString()), ("IsActive", "true"))).StatusCode);
        Assert.False(await db.PodMembers.AnyAsync(m => m.PodId == pod.Id));
        Assert.False(await db.PodApplications.AnyAsync(a => a.PodId == pod.Id));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Admin/Pods", $"/Admin/Pods/Delete/{pod.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/Admin/ApplicationPlatforms", $"/Admin/ApplicationPlatforms/Delete/{app.Id}")).StatusCode);
    }

    [Fact]
    public async Task Assign_and_resync_expand_across_business_units_skip_unpriced_and_preserve_manual_edits()
    {
        var client = Client();
        await client.GetAsync("/");
        int id, podId, phaseId, memberId, joinerId, appId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rate = await db.RateCardEntries.Include(e => e.RateCard).FirstAsync(e => e.ResourcingClassId == ResourcingClass.InternalId);
            var bu = await db.BusinessUnits.FirstAsync();
            Person Person(string name, string? location = null) => new() { DisplayName = name, BusinessUnitId = bu.Id, ResourceTypeId = rate.ResourceTypeId, SeniorityId = rate.SeniorityId, Location = location ?? rate.Location, ResourcingClassId = rate.ResourcingClassId };
            var member = Person("Pod member"); var joiner = Person("Pod joiner"); var missing = Person("Unpriced member", "Unpriced location");
            db.People.Add(joiner);
            var app = new ApplicationPlatform { Name = "Tagged " + Guid.NewGuid().ToString("N") };
            var pod = new Pod { Name = "Assign " + Guid.NewGuid().ToString("N"), BusinessUnitId = bu.Id, Members = [new() { Person = member }, new() { Person = missing }], Applications = [new() { ApplicationPlatform = app }] };
            db.Pods.Add(pod);
            var initiative = new Initiative { Name = "Pod initiative", CreatedBy = "dev-user", BusinessUnit = new BusinessUnit { Name = "Other " + Guid.NewGuid().ToString("N") }, TargetStart = rate.RateCard!.EffectiveStart, Phases = [new Phase { Name = "Build", PlannedStart = rate.RateCard.EffectiveStart, PlannedEnd = rate.RateCard.EffectiveStart.AddDays(20) }] };
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
            id = initiative.Id; podId = pod.Id; phaseId = initiative.Phases.Single().Id; memberId = member.Id; joinerId = joiner.Id; appId = app.Id;
        }
        var page = $"/Initiatives/DevelopmentPods/{id}";
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, page, $"/Initiatives/UpdateApplications/{id}", ("applicationIds", appId.ToString()))).StatusCode);
        Assert.Contains("Assign ", await client.GetStringAsync(page));
        var invalid = await Post(client, page, $"/Initiatives/AssignPod/{id}", ("PodId", podId.ToString()), ("AllocationPercent", "100"));
        Assert.Equal(HttpStatusCode.Redirect, invalid.StatusCode);
        await Post(client, page, $"/Initiatives/AssignPod/{id}", ("PodId", podId.ToString()), ("PhaseIds", phaseId.ToString()), ("AllocationPercent", "not-a-number"));
        using (var scope = factory.Services.CreateScope())
            Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().InitiativeAllocations.AnyAsync(a => a.InitiativeId == id));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, page, $"/Initiatives/AssignPod/{id}", ("PodId", podId.ToString()), ("PhaseIds", phaseId.ToString()), ("AllocationPercent", "50"))).StatusCode);
        Assert.Contains("Unpriced member", await client.GetStringAsync($"/Initiatives/Details/{id}"));
        int allocationId;
        (string Key, string Value)[] edit;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seat = await db.InitiativeAllocations.Include(a => a.People).SingleAsync(a => a.InitiativeId == id);
            allocationId = seat.Id;
            Assert.Equal(memberId, seat.PersonIds.Single()); Assert.Equal(50, seat.AllocationPercent); Assert.Equal(podId, seat.PodId);
            Assert.NotEqual((await db.Initiatives.FindAsync(id))!.BusinessUnitId, seat.BusinessUnitId);
            edit = [("PhaseId", phaseId.ToString()), ("BusinessUnitId", seat.BusinessUnitId.ToString()), ("ResourceTypeId", seat.ResourceTypeId.ToString()), ("SeniorityId", seat.SeniorityId.ToString()), ("Location", seat.Location), ("ResourcingClassId", seat.ResourcingClassId.ToString()), ("PersonIds", memberId.ToString()), ("Quantity", "1"), ("EstimatedHours", "23"), ("CapexPercent", "17")];
            db.PodMembers.Add(new PodMember { PodId = podId, PersonId = joinerId });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/Initiatives/EditAllocation/{allocationId}", $"/Initiatives/EditAllocation/{allocationId}", edit)).StatusCode);
        Assert.Contains("table-light", await client.GetStringAsync($"/Initiatives/Details/{id}?groupBy=pod"));
        var preview = $"/Initiatives/ResyncPod/{id}?podId={podId}";
        var html = await client.GetStringAsync(preview);
        Assert.Contains("Seats to add (1)", html); Assert.Contains("Pod joiner", html);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, preview, $"/Initiatives/ConfirmResyncPod/{id}", ("podId", podId.ToString()))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seat = await db.InitiativeAllocations.FindAsync(allocationId);
            Assert.Equal(23, seat!.EstimatedHours); Assert.Equal(17, seat.CapexPercent);
            Assert.Equal(2, await db.InitiativeAllocations.CountAsync(a => a.InitiativeId == id));
            db.PodMembers.Remove(await db.PodMembers.SingleAsync(m => m.PodId == podId && m.PersonId == memberId));
            await db.SaveChangesAsync();
        }
        Assert.Contains("Seats to remove (1)", await client.GetStringAsync(preview));
        await Post(client, preview, $"/Initiatives/ConfirmResyncPod/{id}", ("podId", podId.ToString()));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.InitiativeAllocations.CountAsync(a => a.InitiativeId == id));
            var initiative = (await db.Initiatives.FindAsync(id))!;
            initiative.Status = InitiativeStatus.Active;
            await db.SaveChangesAsync();
        }
        var locked = await Post(client, $"/Initiatives/Details/{id}", $"/Initiatives/AssignPod/{id}", ("PodId", podId.ToString()), ("PhaseIds", phaseId.ToString()), ("AllocationPercent", "100"));
        Assert.Equal(HttpStatusCode.Redirect, locked.StatusCode);
        Assert.Contains("Scope is locked", await client.GetStringAsync($"/Initiatives/Details/{id}"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(preview)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, $"/Initiatives/Details/{id}", $"/Initiatives/ConfirmResyncPod/{id}", ("podId", podId.ToString()))).StatusCode);
    }

    [Fact]
    public async Task Viewer_cannot_manage_catalogs_or_assign_pods()
    {
        await using var viewer = new ViewerOnlyFactory();
        var client = viewer.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Pods")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/ApplicationPlatforms")).StatusCode);
        int id;
        using (var scope = viewer.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var initiative = new Initiative { Name = "Viewer pod test", CreatedBy = "other", BusinessUnitId = (await db.BusinessUnits.FirstAsync()).Id };
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
            id = initiative.Id;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, $"/Initiatives/Details/{id}", $"/Initiatives/AssignPod/{id}", ("PodId", "1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, $"/Initiatives/Details/{id}", $"/Initiatives/ConfirmResyncPod/{id}", ("podId", "1"))).StatusCode);
    }
}
