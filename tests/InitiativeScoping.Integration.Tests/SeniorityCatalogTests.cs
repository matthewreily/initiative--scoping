using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class SeniorityCatalogTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Seeds_default_ladder_in_order()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var names = await db.SeniorityLevels.Where(s => DbSeeder.DefaultSeniorityLevels.Contains(s.Name)).OrderBy(s => s.SortOrder).Select(s => s.Name).ToListAsync();
        Assert.Equal(DbSeeder.DefaultSeniorityLevels, names);
    }

    [Fact]
    public async Task Catalog_crud_is_audited_and_delete_is_guarded_by_references()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Level 9 ({Guid.NewGuid():N})";

        var created = await PostFormAsync(client, "/Admin/Seniorities/Create", "/Admin/Seniorities/Create", new() { ["Name"] = name, ["SortOrder"] = "9", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var duplicate = await PostFormAsync(client, "/Admin/Seniorities/Create", "/Admin/Seniorities/Create", new() { ["Name"] = name.ToUpperInvariant(), ["SortOrder"] = "9", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        int levelId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var level = await db.SeniorityLevels.SingleAsync(s => s.Name == name);
            levelId = level.Id;
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(SeniorityLevel) && a.EntityId == levelId.ToString() && a.Action == "Create"));
        }

        var edited = await PostFormAsync(client, $"/Admin/Seniorities/Edit/{levelId}", $"/Admin/Seniorities/Edit/{levelId}",
            new() { ["Id"] = levelId.ToString(), ["Name"] = name + " renamed", ["SortOrder"] = "10", ["IsActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Contains(name + " renamed", await client.GetStringAsync("/Admin/Seniorities"));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var type = await db.ResourceTypes.FirstAsync();
            var card = await db.RateCards.FirstAsync();
            db.RateCardEntries.Add(new RateCardEntry
            {
                RateCardId = card.Id, ResourceTypeId = type.Id, SeniorityId = levelId,
                Location = "Moon", ResourcingClass = ResourcingClass.InternalFte, HourlyRate = 1m
            });
            await db.SaveChangesAsync();
        }

        var guarded = await PostFormAsync(client, "/Admin/Seniorities", $"/Admin/Seniorities/Delete/{levelId}", new());
        Assert.Equal(HttpStatusCode.Redirect, guarded.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(await db.SeniorityLevels.FindAsync(levelId));
            db.RateCardEntries.RemoveRange(db.RateCardEntries.Where(e => e.SeniorityId == levelId));
            await db.SaveChangesAsync();
        }

        var deleted = await PostFormAsync(client, "/Admin/Seniorities", $"/Admin/Seniorities/Delete/{levelId}", new());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.SeniorityLevels.FindAsync(levelId));
        }
    }

    [Fact]
    public async Task Rate_card_csv_import_adds_unknown_seniority_names_to_the_catalog_atomically()
    {
        var client = factory.CreateClient(NoRedirect);
        var create = await PostFormAsync(client, "/Admin/RateCards/Create", "/Admin/RateCards/Create",
            new() { ["Name"] = $"Vend-{Guid.NewGuid():N}"[..14], ["EffectiveStart"] = "2027-07-01" });
        var detailsUrl = create.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last());
        var tag = Guid.NewGuid().ToString("N")[..6];
        var level1 = $"Level 1 (0-2 Years) {tag}";
        var level2 = $"Level 2 (3-5 Years) {tag}";

        // A rejected file (unknown resource type) must not add the seniority level either.
        var bad = $"ResourceType,Seniority,Location,ResourcingClass,HourlyRate\nNope,{level1},Onshore,Internal,100\n";
        await PostCsvAsync(client, detailsUrl, $"/Admin/RateCards/Import/{id}", bad);
        Assert.Contains("Import rejected", await client.GetStringAsync(detailsUrl));
        Assert.Equal(0, await LevelCountAsync(level1));

        var good = "ResourceType,Seniority,Location,ResourcingClass,HourlyRate\n" +
                   $"Software Engineer,{level1},Onshore,Vendor,80\n" +
                   $"Software Engineer,{level2},Onshore,Vendor,95\n" +
                   $"QA Analyst,{level1.ToUpperInvariant()},Onshore,Vendor,60\n";
        await PostCsvAsync(client, detailsUrl, $"/Admin/RateCards/Import/{id}", good);
        var page = await client.GetStringAsync(detailsUrl);
        Assert.Contains("3 added", page);
        Assert.Contains("New seniority level(s) added", page);
        Assert.Equal(1, await LevelCountAsync(level1));
        Assert.Equal(1, await LevelCountAsync(level2));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var level = await db.SeniorityLevels.SingleAsync(s => s.Name == level1);
            Assert.True(level.IsActive);
            Assert.Equal(2, await db.RateCardEntries.CountAsync(e => e.RateCardId == id && e.SeniorityId == level.Id));
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(SeniorityLevel) && a.EntityId == level.Id.ToString() && a.Action == "Create"));
        }

        // Re-importing the same names reuses the catalog rows and updates rates instead of duplicating.
        await PostCsvAsync(client, detailsUrl, $"/Admin/RateCards/Import/{id}", good.Replace(",80\n", ",85\n"));
        Assert.Contains("0 added, 1 updated", await client.GetStringAsync(detailsUrl));
        Assert.Equal(1, await LevelCountAsync(level1));

        var export = await client.GetAsync($"/Admin/RateCards/Export/{id}");
        export.EnsureSuccessStatusCode();
        Assert.Contains($"Software Engineer,{level1},Onshore,Vendor,85", await export.Content.ReadAsStringAsync());
        Assert.Contains(level1, await client.GetStringAsync("/Admin/Seniorities"));
    }

    [Fact]
    public async Task People_csv_import_adds_unknown_seniority_names_to_the_catalog()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var level = $"Grade B {tag}";
        var csv = "DisplayName,ExternalIds,ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,IsActive\n" +
                  $"Pat {tag},PAT-{tag},Software Engineer,Boarding,{level},Onshore,Internal,true\n";

        var content = new MultipartFormDataContent { { new StringContent(await GetTokenAsync(client, "/Admin/People")), "__RequestVerificationToken" } };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "people.csv");
        var response = await client.PostAsync("/Admin/People/Import", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var person = await db.People.Include(p => p.Seniority).SingleAsync(p => p.DisplayName == $"Pat {tag}");
        Assert.Equal(level, person.Seniority!.Name);
    }

    [Fact]
    public async Task Allocation_form_lists_catalog_levels_and_rejects_unknown_ids()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Ladder {Guid.NewGuid():N}"[..20];
        int levelId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var level = new SeniorityLevel { Name = name, SortOrder = 50 };
            db.SeniorityLevels.Add(level);
            await db.SaveChangesAsync();
            levelId = level.Id;
        }

        int buId, typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            buId = await db.BusinessUnits.Select(b => b.Id).FirstAsync();
            typeId = await db.ResourceTypes.Select(t => t.Id).FirstAsync();
        }

        var created = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = $"Sen {Guid.NewGuid():N}"[..12], ["BusinessUnitId"] = buId.ToString(),
            ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2027-01-04"
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var detailsUrl = created.Headers.Location!.ToString();
        var initiativeId = int.Parse(detailsUrl.Split('/').Last());

        var addPhase = await PostFormAsync(client, detailsUrl, $"/Initiatives/AddPhase/{initiativeId}", new()
        {
            ["Name"] = "Build", ["PlannedStart"] = "2027-01-04", ["PlannedEnd"] = "2027-01-29"
        });
        Assert.Equal(HttpStatusCode.Redirect, addPhase.StatusCode);
        Assert.Contains(name, await client.GetStringAsync(detailsUrl));
        int phaseId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = await db.Phases.Where(p => p.InitiativeId == initiativeId).Select(p => p.Id).SingleAsync();
        }

        var unknown = await PostFormAsync(client, detailsUrl, $"/Initiatives/AddAllocation/{initiativeId}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "999999",
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "10"
        });
        Assert.Equal(HttpStatusCode.Redirect, unknown.StatusCode);
        Assert.Contains("Select a seniority level", await client.GetStringAsync(detailsUrl));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.InitiativeAllocations.AnyAsync(a => a.InitiativeId == initiativeId));
        }

        var ok = await PostFormAsync(client, detailsUrl, $"/Initiatives/AddAllocation/{initiativeId}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = levelId.ToString(),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "10"
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Contains(name, await client.GetStringAsync(detailsUrl));
    }

    private async Task<int> LevelCountAsync(string name)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SeniorityLevels.CountAsync(s => s.Name == name);
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {pageUrl}");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetTokenAsync(client, tokenPage);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }

    private static async Task<HttpResponseMessage> PostCsvAsync(HttpClient client, string tokenPage, string postUrl, string csv)
    {
        var content = new MultipartFormDataContent { { new StringContent(await GetTokenAsync(client, tokenPage)), "__RequestVerificationToken" } };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "rates.csv");
        return await client.PostAsync(postUrl, content);
    }
}
