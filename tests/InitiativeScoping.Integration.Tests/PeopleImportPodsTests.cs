using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.People;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class PeopleImportPodsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private const string Header = "DisplayName,ExternalIds,ResourceType,BusinessUnit,Seniority,Location,ResourcingClass";

    [Fact]
    public async Task Import_creates_and_reuses_pods_case_insensitively_with_memberships_audits_and_summary()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/Admin/People");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var shared = "Shared " + tag;
        var defaultPod = "Default " + tag;
        var otherUnit = "Other unit " + tag;
        Pod existing;
        int boardingId, otherUnitId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            boardingId = await db.BusinessUnits.Where(b => b.Name == "Boarding").Select(b => b.Id).SingleAsync();
            existing = new Pod { Name = "Existing " + tag, BusinessUnit = new BusinessUnit { Name = otherUnit } };
            db.Pods.Add(existing);
            await db.SaveChangesAsync();
            otherUnitId = existing.BusinessUnitId;
        }
        var csv = $"{Header},Pods\n" +
            $"One {tag},ONE-{tag},Software Engineer,Boarding,Senior,Onshore,Internal, {shared} : 40 ; {existing.Name.ToUpperInvariant()}:60; {defaultPod}\n" +
            $"Two {tag},TWO-{tag},Software Engineer,{otherUnit},Mid,Onshore,Internal,{shared.ToUpperInvariant()}:25\n";
        var summary = await Import(client, csv);
        Assert.Contains("Import complete: 2 added", summary);
        Assert.Contains("Pods created (2):", summary);
        Assert.Contains(shared, summary);
        Assert.Contains(defaultPod, summary);
        Assert.Contains("Pod memberships added (4):", summary);
        Assert.Contains($"One {tag}: 200% total membership across pods (over 100%).", summary);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pod = await db.Pods.Include(p => p.Members).ThenInclude(m => m.Person).SingleAsync(p => p.Name == shared);
            Assert.Equal(1, await db.Pods.CountAsync(p => p.Name.ToLower() == shared.ToLower()));
            Assert.True(pod.IsActive);
            Assert.Equal(boardingId, pod.BusinessUnitId);
            Assert.Equal(2, pod.Members.Count);
            Assert.Equal(40, pod.Members.Single(m => m.Person!.DisplayName == $"One {tag}").MembershipPercent);
            Assert.Equal(25, pod.Members.Single(m => m.Person!.DisplayName == $"Two {tag}").MembershipPercent);
            Assert.Equal(otherUnitId, (await db.Pods.FindAsync(existing.Id))!.BusinessUnitId);
            Assert.Equal(60, (await db.PodMembers.SingleAsync(m => m.PodId == existing.Id)).MembershipPercent);
            var defaultMembership = await db.PodMembers.Include(m => m.Pod).SingleAsync(m => m.Pod!.Name == defaultPod);
            Assert.Equal(100, defaultMembership.MembershipPercent);
            var creation = await db.AuditEvents.SingleAsync(a => a.Entity == nameof(Pod) && a.EntityId == pod.Id.ToString() && a.Action == AuditActions.Create);
            using var created = JsonDocument.Parse(creation.DiffJson!);
            Assert.Equal(boardingId, created.RootElement.GetProperty("BusinessUnitId").GetInt32());
            Assert.True(created.RootElement.GetProperty("IsActive").GetBoolean());
            var membershipAudit = await db.AuditEvents.SingleAsync(a => a.Entity == nameof(Pod) && a.EntityId == pod.Id.ToString() && a.Action == AuditActions.Update);
            using var membership = JsonDocument.Parse(membershipAudit.DiffJson!);
            Assert.Equal(0, membership.RootElement.GetProperty("Before").GetProperty("Memberships").GetArrayLength());
            Assert.Equal(2, membership.RootElement.GetProperty("After").GetProperty("Memberships").GetArrayLength());
            var importAudit = await db.AuditEvents.Where(a => a.Entity == nameof(Person) && a.Action == AuditActions.Import).OrderByDescending(a => a.Id).FirstAsync();
            using var imported = JsonDocument.Parse(importAudit.DiffJson!);
            Assert.Equal(2, imported.RootElement.GetProperty("Pods").GetProperty("CreatedPods").GetArrayLength());
            Assert.Equal(4, imported.RootElement.GetProperty("Pods").GetProperty("MembershipsAdded").GetArrayLength());
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var classes = await db.ResourcingClasses.ToListAsync();
            var exported = PeopleCsv.Parse(new StringReader(await client.GetStringAsync("/Admin/People/Export")), classes);
            Assert.True(exported.IsValid);
            Assert.Contains(new PeopleCsvPod(defaultPod, 100), exported.Rows.Single(r => r.DisplayName == $"One {tag}").Pods!);
            var template = PeopleCsv.Parse(new StringReader(await client.GetStringAsync("/Admin/People/Template")), classes);
            Assert.True(template.IsValid);
            var pods = template.Rows.First().Pods!;
            Assert.Contains(pods, p => p.MembershipPercent == 40);
            Assert.Contains(pods, p => p.MembershipPercent is null);
        }
    }

    [Fact]
    public async Task Import_is_additive_preserves_implicit_percentages_and_warns_for_inactive_pods()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/Admin/People");
        var tag = Guid.NewGuid().ToString("N")[..8];
        Person person;
        Pod[] pods;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            person = new Person
            {
                DisplayName = "Additive " + tag, ExternalIds = tag, Location = "Onshore", ResourcingClassId = ResourcingClass.InternalId,
                ResourceTypeId = await db.ResourceTypes.Where(t => t.Name == "Software Engineer").Select(t => t.Id).SingleAsync(),
                SeniorityId = await db.SeniorityLevels.Where(s => s.Name == "Mid").Select(s => s.Id).SingleAsync(),
                BusinessUnitId = await db.BusinessUnits.Where(b => b.Name == "Boarding").Select(b => b.Id).SingleAsync()
            };
            pods = new[] { "Unlisted", "Explicit", "Implicit", "Inactive" }.Select((name, i) => new Pod
            {
                Name = $"{name} {tag}", BusinessUnitId = person.BusinessUnitId, IsActive = i != 3,
                Members = [new PodMember { Person = person, MembershipPercent = new[] { 20m, 30m, 40m, 10m }[i] }]
            }).ToArray();
            db.Pods.AddRange(pods);
            await db.SaveChangesAsync();
        }
        var row = $"{person.DisplayName},{tag},Software Engineer,Boarding,Mid,Onshore,Internal";
        var summary = await Import(client, $"{Header},Development Pod\n{row},{pods[1].Name}:60;{pods[2].Name};{pods[3].Name.ToUpperInvariant()}:45\n");
        Assert.Contains("Pod memberships updated (2):", summary);
        Assert.Contains("30% → 60%", summary);
        Assert.Contains("Warning: inactive pods used (not reactivated) (1): " + pods[3].Name, summary);
        Assert.Contains("165% total membership across pods (over 100%).", summary);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var memberships = await db.PodMembers.Where(m => m.PersonId == person.Id).ToDictionaryAsync(m => m.PodId);
            Assert.Equal(4, memberships.Count);
            Assert.Equal(new[] { 20m, 60m, 40m, 45m }, pods.Select(p => memberships[p.Id].MembershipPercent));
            Assert.False((await db.Pods.FindAsync(pods[3].Id))!.IsActive);
            Assert.False(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Pod) && (a.EntityId == pods[0].Id.ToString() || a.EntityId == pods[2].Id.ToString())));
            var change = await db.AuditEvents.SingleAsync(a => a.Entity == nameof(Pod) && a.EntityId == pods[1].Id.ToString());
            using var json = JsonDocument.Parse(change.DiffJson!);
            Assert.Equal(30, json.RootElement.GetProperty("Before").GetProperty("Memberships")[0].GetProperty("MembershipPercent").GetDecimal());
            Assert.Equal(60, json.RootElement.GetProperty("After").GetProperty("Memberships")[0].GetProperty("MembershipPercent").GetDecimal());
        }
        foreach (var csv in new[] { $"{Header},Pod\n{row},\n", $"{Header}\n{row}\n", $"{Header},Pods\n{row},{pods[1].Name}:60;{pods[2].Name}\n" })
        {
            var unchanged = await Import(client, csv);
            Assert.DoesNotContain("Pod memberships added", unchanged);
            Assert.DoesNotContain("Pod memberships updated", unchanged);
        }
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(4, await db.PodMembers.CountAsync(m => m.PersonId == person.Id));
            Assert.Equal(2, await db.AuditEvents.CountAsync(a => a.Entity == nameof(Pod) && (a.EntityId == pods[1].Id.ToString() || a.EntityId == pods[3].Id.ToString())));
        }
    }

    [Fact]
    public async Task Long_summary_lists_are_bounded_but_full_details_are_audited()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tag = Guid.NewGuid().ToString("N")[..8];
        var pods = Enumerable.Range(1, 6).Select(i => $"Bulk {tag} {i}").ToArray();
        var summary = await Import(client, $"{Header},Pods\nBulk {tag},BULK-{tag},Software Engineer,Boarding,Mid,Onshore,Internal,{string.Join(';', pods)}\n");
        Assert.Contains("Pods created (6):", summary);
        Assert.Contains("Pod memberships added (6):", summary);
        Assert.Contains("(+1 more)", summary);
        Assert.DoesNotContain(pods[5], summary);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditEvents.Where(a => a.Entity == nameof(Person) && a.Action == AuditActions.Import).OrderByDescending(a => a.Id).FirstAsync();
        using var json = JsonDocument.Parse(audit.DiffJson!);
        var details = json.RootElement.GetProperty("Pods");
        Assert.Equal(6, details.GetProperty("CreatedPods").GetArrayLength());
        Assert.Equal(6, details.GetProperty("MembershipsAdded").GetArrayLength());
        Assert.Equal(pods[5], details.GetProperty("CreatedPods")[5].GetString());
    }

    [Theory]
    [InlineData(";")]
    [InlineData(":")]
    public async Task Export_blocks_legacy_delimiter_names_then_round_trips_memberships_after_renaming(string delimiter)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.GetAsync("/Admin/People");
        var tag = Guid.NewGuid().ToString("N")[..8];
        var invalidName = $"Legacy{delimiter}{tag}";
        var safeName = "Renamed " + tag;
        int podId, personId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var person = await db.People.FirstAsync();
            personId = person.Id;
            var pod = new Pod { Name = invalidName, BusinessUnitId = person.BusinessUnitId,
                Members = [new PodMember { PersonId = personId, MembershipPercent = 37.5m }] };
            db.Pods.Add(pod);
            await db.SaveChangesAsync();
            podId = pod.Id;
        }
        var blocked = await client.GetAsync("/Admin/People/Export");
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Null(blocked.Content.Headers.ContentDisposition);
        var warning = WebUtility.HtmlDecode(await client.GetStringAsync(blocked.Headers.Location!));
        Assert.Contains("People CSV export blocked", warning);
        Assert.Contains(invalidName, warning);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Pods.FindAsync(podId))!.Name = safeName;
            await db.SaveChangesAsync();
        }
        var csv = await client.GetStringAsync("/Admin/People/Export");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parsed = PeopleCsv.Parse(new StringReader(csv), await db.ResourcingClasses.ToListAsync());
            Assert.True(parsed.IsValid);
            Assert.Contains(parsed.Rows, r => (r.Pods ?? []).Contains(new PeopleCsvPod(safeName, 37.5m)));
        }
        Assert.Contains("Import complete:", await Import(client, csv));
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(37.5m, (await context.PodMembers.SingleAsync(m => m.PodId == podId && m.PersonId == personId)).MembershipPercent);
        Assert.Equal(1, await context.Pods.CountAsync(p => p.Name == safeName));
        Assert.False(await context.Pods.AnyAsync(p => p.Name == invalidName));
    }

    [Theory]
    [InlineData("Broken:0")]
    [InlineData("Broken:101")]
    [InlineData("Broken:invalid")]
    [InlineData(":40")]
    [InlineData("Broken:40:50")]
    [InlineData("Broken;;Other")]
    [InlineData("Repeat; repeat:50")]
    public async Task Invalid_pods_reject_the_whole_file_without_catalog_person_or_membership_changes(string invalid)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var tag = Guid.NewGuid().ToString("N")[..8];
        var pod = "Not created " + tag;
        var level = "Not created level " + tag;
        var csv = $"{Header},Pods\n" +
            $"Good {tag},GOOD-{tag},Software Engineer,Boarding,{level},Onshore,Internal,{pod}:40\n" +
            $"Bad {tag},BAD-{tag},Software Engineer,Boarding,Mid,Onshore,Internal,{invalid}\n";
        var summary = await Import(client, csv);
        Assert.Contains("Import rejected; no changes made.", summary);
        Assert.Contains("Line 3:", summary);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Pods.AnyAsync(p => p.Name == pod));
        Assert.False(await db.People.AnyAsync(p => p.DisplayName == $"Good {tag}" || p.DisplayName == $"Bad {tag}"));
        Assert.False(await db.SeniorityLevels.AnyAsync(s => s.Name == level));
    }

    private static async Task<string> Import(HttpClient client, string csv)
    {
        var html = await client.GetStringAsync("/Admin/People");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);
        using var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "people.csv");
        var response = await client.PostAsync("/Admin/People/Import", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!));
    }
}
