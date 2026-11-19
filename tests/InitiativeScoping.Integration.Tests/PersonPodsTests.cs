using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class PersonPodsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Person_create_and_edit_add_remove_retain_percentages_and_audit_pod_memberships()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/");
        var name = "Person pods " + Guid.NewGuid().ToString("N");
        List<KeyValuePair<string, string>> fields;
        Pod[] pods;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var template = await db.People.FirstAsync();
            fields = new Dictionary<string, string>
            {
                ["DisplayName"] = name, ["ResourceTypeId"] = template.ResourceTypeId.ToString(),
                ["BusinessUnitId"] = template.BusinessUnitId.ToString(), ["SeniorityId"] = template.SeniorityId.ToString(),
                ["Location"] = template.Location, ["ResourcingClassId"] = template.ResourcingClassId.ToString(), ["IsActive"] = "true"
            }.ToList();
            pods = Enumerable.Range(0, 5).Select(i => new Pod { Name = $"{name} pod {i}", BusinessUnitId = template.BusinessUnitId, IsActive = i != 2 && i != 3 }).ToArray();
            db.Pods.AddRange(pods);
            await db.SaveChangesAsync();
        }
        var create = "/Admin/People/Create";
        var createHtml = await client.GetStringAsync(create);
        Assert.Contains(pods[0].Name, createHtml);
        Assert.DoesNotContain(pods[3].Name, createHtml);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, create, fields, pods[0].Id, pods[1].Id)).StatusCode);
        int personId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            personId = (await db.People.SingleAsync(p => p.DisplayName == name)).Id;
            var members = await db.PodMembers.Where(m => m.PersonId == personId).OrderBy(m => m.PodId).ToListAsync();
            Assert.Equal(2, members.Count);
            Assert.All(members, m => Assert.Equal(100, m.MembershipPercent));
            members.Single(m => m.PodId == pods[0].Id).MembershipPercent = 40;
            members.Single(m => m.PodId == pods[1].Id).MembershipPercent = 60;
            db.PodMembers.Add(new PodMember { PodId = pods[2].Id, PersonId = personId, MembershipPercent = 37.5m });
            await db.SaveChangesAsync();
        }
        var edit = $"/Admin/People/Edit/{personId}";
        var html = await client.GetStringAsync(edit);
        Assert.Contains(pods[2].Name, html);
        Assert.Contains("(inactive)", html);
        Assert.Contains("37.5%", html);
        Assert.DoesNotContain(pods[3].Name, html);
        Assert.Matches($"name=\"PodIds\" value=\"{pods[2].Id}\" checked", html);
        var invalid = await Post(client, edit, fields, pods[0].Id, pods[3].Id);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Choose active pods", await invalid.Content.ReadAsStringAsync());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(3, await db.PodMembers.CountAsync(m => m.PersonId == personId));
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, edit, fields, pods[0].Id, pods[2].Id, pods[4].Id, pods[4].Id)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var members = await db.PodMembers.Where(m => m.PersonId == personId).ToDictionaryAsync(m => m.PodId);
            Assert.Equal(3, members.Count);
            Assert.Equal(40, members[pods[0].Id].MembershipPercent);
            Assert.Equal(37.5m, members[pods[2].Id].MembershipPercent);
            Assert.Equal(100, members[pods[4].Id].MembershipPercent);
            Assert.False(members.ContainsKey(pods[1].Id));
            var events = await db.AuditEvents.Where(a => a.Entity == nameof(Pod)).OrderBy(a => a.Id).ToListAsync();
            var removed = events.Last(a => a.EntityId == pods[1].Id.ToString());
            using var diff = JsonDocument.Parse(removed.DiffJson!);
            Assert.Equal("PersonMembership", diff.RootElement.GetProperty("Operation").GetString());
            Assert.Equal(60, diff.RootElement.GetProperty("Before").GetProperty("Memberships")[0].GetProperty("MembershipPercent").GetDecimal());
            Assert.Equal(0, diff.RootElement.GetProperty("After").GetProperty("Memberships").GetArrayLength());
            var added = events.Single(a => a.EntityId == pods[4].Id.ToString());
            using var addition = JsonDocument.Parse(added.DiffJson!);
            Assert.Equal(100, addition.RootElement.GetProperty("After").GetProperty("Memberships")[0].GetProperty("MembershipPercent").GetDecimal());
            Assert.Single(events.Where(a => a.EntityId == pods[0].Id.ToString()));
            Assert.DoesNotContain(events, a => a.EntityId == pods[2].Id.ToString());
        }
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, edit, fields)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.PodMembers.AnyAsync(m => m.PersonId == personId));
        }
    }

    [Fact]
    public async Task Invalid_create_cannot_add_inactive_or_unknown_pods_and_preserves_valid_selection()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/");
        Pod active, inactive;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bu = (await db.BusinessUnits.FirstAsync()).Id;
            active = new Pod { Name = "Valid " + Guid.NewGuid(), BusinessUnitId = bu };
            inactive = new Pod { Name = "Inactive " + Guid.NewGuid(), BusinessUnitId = bu, IsActive = false };
            db.Pods.AddRange(active, inactive);
            await db.SaveChangesAsync();
        }
        var response = await Post(client, "/Admin/People/Create", [], active.Id, inactive.Id, int.MaxValue);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Choose active pods", html);
        Assert.Matches($"name=\"PodIds\" value=\"{active.Id}\" checked", html);
        using var check = factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await context.PodMembers.AnyAsync(m => m.PodId == active.Id || m.PodId == inactive.Id));
    }

    private static async Task<HttpResponseMessage> Post(HttpClient client, string page, List<KeyValuePair<string, string>> fields, params int[] podIds)
    {
        var html = await client.GetStringAsync(page);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        return await client.PostAsync(page, new FormUrlEncodedContent(fields
            .Concat(podIds.Select(id => new KeyValuePair<string, string>("PodIds", id.ToString())))
            .Append(new("__RequestVerificationToken", WebUtility.HtmlDecode(token)))));
    }
}
