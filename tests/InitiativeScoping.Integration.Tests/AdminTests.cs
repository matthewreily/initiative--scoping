using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class ViewerOnlyFactory : WebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:Dev:Roles:0", "Viewer");
        for (var i = 1; i < 5; i++)
        {
            builder.UseSetting($"Auth:Dev:Roles:{i}", "Viewer");
        }
    }
}

public class AdminTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Admin_pages_render_for_administrator()
    {
        var client = factory.CreateClient(NoRedirect);
        foreach (var path in new[] { "/Admin/BusinessUnits", "/Admin/Disciplines", "/Admin/ResourceTypes", "/Admin/RateCards", "/Admin/Sizing" })
        {
            var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path} returned {(int)response.StatusCode}");
        }
    }

    [Fact]
    public async Task Admin_pages_are_forbidden_for_viewer()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        var client = viewerFactory.CreateClient();
        var response = await client.GetAsync("/Admin/BusinessUnits");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_business_unit_writes_audit_event()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"BU-{Guid.NewGuid():N}"[..12];
        var response = await PostFormAsync(client, "/Admin/BusinessUnits/Create", "/Admin/BusinessUnits/Create",
            new() { ["Name"] = name, ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unit = await db.BusinessUnits.SingleAsync(b => b.Name == name);
        var audit = await db.AuditEvents.SingleAsync(a => a.Entity == nameof(BusinessUnit) && a.EntityId == unit.Id.ToString() && a.Action == "Create");
        Assert.Equal("dev-user", audit.UserId);
        Assert.Contains(name, audit.DiffJson);
    }

    [Fact]
    public async Task Resource_type_names_are_unique_case_insensitively()
    {
        var client = factory.CreateClient(NoRedirect);
        var disciplineId = await SeededDisciplineIdAsync();
        var name = $"Type-{Guid.NewGuid():N}"[..14];
        var first = await PostFormAsync(client, "/Admin/ResourceTypes/Create", "/Admin/ResourceTypes/Create",
            new() { ["Name"] = name, ["DisciplineId"] = disciplineId.ToString(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        var second = await PostFormAsync(client, "/Admin/ResourceTypes/Create", "/Admin/ResourceTypes/Create",
            new() { ["Name"] = name.ToUpperInvariant(), ["DisciplineId"] = disciplineId.ToString(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("already exists", await second.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = await db.ResourceTypes.SingleAsync(t => t.Name == name);
        Assert.Equal(disciplineId, created.DisciplineId);
    }

    [Fact]
    public async Task Resource_type_requires_an_existing_active_discipline()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Type-{Guid.NewGuid():N}"[..14];

        var missing = await PostFormAsync(client, "/Admin/ResourceTypes/Create", "/Admin/ResourceTypes/Create",
            new() { ["Name"] = name, ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("Select a discipline", await missing.Content.ReadAsStringAsync());

        var unknown = await PostFormAsync(client, "/Admin/ResourceTypes/Create", "/Admin/ResourceTypes/Create",
            new() { ["Name"] = name, ["DisciplineId"] = "999999", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Contains("Select an active discipline", await unknown.Content.ReadAsStringAsync());

        int inactiveId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var inactive = new Discipline { Name = $"Inactive-{Guid.NewGuid():N}"[..20], IsActive = false };
            db.Disciplines.Add(inactive);
            await db.SaveChangesAsync();
            inactiveId = inactive.Id;
        }

        var inactiveResponse = await PostFormAsync(client, "/Admin/ResourceTypes/Create", "/Admin/ResourceTypes/Create",
            new() { ["Name"] = name, ["DisciplineId"] = inactiveId.ToString(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, inactiveResponse.StatusCode);
        Assert.Contains("Select an active discipline", await inactiveResponse.Content.ReadAsStringAsync());

        using var verify = factory.Services.CreateScope();
        Assert.False(await verify.ServiceProvider.GetRequiredService<AppDbContext>().ResourceTypes.AnyAsync(t => t.Name == name));
    }

    [Fact]
    public async Task Resource_type_assigned_to_inactive_discipline_stays_editable()
    {
        var client = factory.CreateClient(NoRedirect);
        int typeId, disciplineId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var discipline = new Discipline { Name = $"Legacy-{Guid.NewGuid():N}"[..20] };
            var type = new ResourceType { Name = $"Type-{Guid.NewGuid():N}"[..14], Discipline = discipline };
            db.ResourceTypes.Add(type);
            await db.SaveChangesAsync();
            discipline.IsActive = false;
            await db.SaveChangesAsync();
            typeId = type.Id;
            disciplineId = discipline.Id;
        }

        var page = await client.GetAsync($"/Admin/ResourceTypes/Edit/{typeId}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("(inactive)", html);
        Assert.Contains($"value=\"{disciplineId}\"", html);

        var renamed = $"Renamed-{Guid.NewGuid():N}"[..14];
        var save = await PostFormAsync(client, $"/Admin/ResourceTypes/Edit/{typeId}", $"/Admin/ResourceTypes/Edit/{typeId}",
            new() { ["Name"] = renamed, ["DisciplineId"] = disciplineId.ToString(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, save.StatusCode);

        using var verify = factory.Services.CreateScope();
        var saved = await verify.ServiceProvider.GetRequiredService<AppDbContext>().ResourceTypes.SingleAsync(t => t.Id == typeId);
        Assert.Equal(renamed, saved.Name);
        Assert.Equal(disciplineId, saved.DisciplineId);
    }

    [Fact]
    public async Task Discipline_crud_is_audited_and_names_are_unique()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Disc-{Guid.NewGuid():N}"[..14];
        var create = await PostFormAsync(client, "/Admin/Disciplines/Create", "/Admin/Disciplines/Create",
            new() { ["Name"] = $" {name} ", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var duplicate = await PostFormAsync(client, "/Admin/Disciplines/Create", "/Admin/Disciplines/Create",
            new() { ["Name"] = name.ToUpperInvariant(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var discipline = await db.Disciplines.SingleAsync(d => d.Name == name);
            id = discipline.Id;
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Discipline) && a.EntityId == id.ToString() && a.Action == "Create"));
        }

        var edit = await PostFormAsync(client, $"/Admin/Disciplines/Edit/{id}", $"/Admin/Disciplines/Edit/{id}",
            new() { ["Name"] = name, ["IsActive"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

        var delete = await PostFormAsync(client, "/Admin/Disciplines", $"/Admin/Disciplines/Delete/{id}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await verifyDb.Disciplines.FindAsync(id));
        var actions = await verifyDb.AuditEvents.Where(a => a.Entity == nameof(Discipline) && a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Equal(["Create", "Delete", "Update"], actions.Order());
    }

    [Fact]
    public async Task Referenced_discipline_cannot_be_deleted()
    {
        var client = factory.CreateClient(NoRedirect);
        var disciplineId = await SeededDisciplineIdAsync();

        var response = await PostFormAsync(client, "/Admin/Disciplines", $"/Admin/Disciplines/Delete/{disciplineId}", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verify = factory.Services.CreateScope();
        Assert.NotNull(await verify.ServiceProvider.GetRequiredService<AppDbContext>().Disciplines.FindAsync(disciplineId));
    }

    private async Task<int> SeededDisciplineIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ResourceTypes.Select(t => t.DisciplineId).FirstAsync();
    }

    [Fact]
    public async Task Referenced_business_unit_cannot_be_deleted()
    {
        var client = factory.CreateClient(NoRedirect);
        int seededId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            seededId = await db.RateCardEntries.Select(e => e.BusinessUnitId).FirstAsync();
        }

        var response = await PostFormAsync(client, "/Admin/BusinessUnits/Create", $"/Admin/BusinessUnits/Delete/{seededId}", new());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var verify = factory.Services.CreateScope();
        Assert.NotNull(await verify.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FindAsync(seededId));
    }

    [Fact]
    public async Task Rate_card_lifecycle_draft_publish_retire()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Card-{Guid.NewGuid():N}"[..14];
        var create = await PostFormAsync(client, "/Admin/RateCards/Create", "/Admin/RateCards/Create",
            new() { ["Name"] = name, ["EffectiveStart"] = "2027-01-01" });
        var detailsUrl = create.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last());

        // Publishing an empty draft is rejected.
        await PostFormAsync(client, detailsUrl, $"/Admin/RateCards/Publish/{id}", new());
        Assert.Equal(RateCardStatus.Draft, await StatusAsync(id));

        int typeId, unitId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            typeId = await db.ResourceTypes.Select(t => t.Id).FirstAsync();
            unitId = await db.BusinessUnits.Select(b => b.Id).FirstAsync();
        }

        await PostFormAsync(client, detailsUrl, $"/Admin/RateCards/AddEntry/{id}", new()
        {
            ["ResourceTypeId"] = typeId.ToString(), ["BusinessUnitId"] = unitId.ToString(), ["Seniority"] = "Senior",
            ["Location"] = "Onshore", ["ResourcingClass"] = "InternalFte", ["HourlyRate"] = "150"
        });
        await PostFormAsync(client, detailsUrl, $"/Admin/RateCards/Publish/{id}", new());
        Assert.Equal(RateCardStatus.Published, await StatusAsync(id));

        // Published cards cannot be deleted, only retired.
        await PostFormAsync(client, detailsUrl, $"/Admin/RateCards/Delete/{id}", new());
        Assert.Equal(RateCardStatus.Published, await StatusAsync(id));

        await PostFormAsync(client, detailsUrl, $"/Admin/RateCards/Retire/{id}", new());
        Assert.Equal(RateCardStatus.Retired, await StatusAsync(id));

        using var audit = factory.Services.CreateScope();
        var actions = await audit.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents
            .Where(a => a.Entity == nameof(RateCard) && a.EntityId == id.ToString())
            .Select(a => a.Action).ToListAsync();
        Assert.Equal(["Create", "Publish", "Retire"], actions);
    }

    [Fact]
    public async Task Csv_import_rejects_unknown_resource_type_and_merges_valid_rows()
    {
        var client = factory.CreateClient(NoRedirect);
        var create = await PostFormAsync(client, "/Admin/RateCards/Create", "/Admin/RateCards/Create",
            new() { ["Name"] = $"Import-{Guid.NewGuid():N}"[..14], ["EffectiveStart"] = "2027-06-01" });
        var detailsUrl = create.Headers.Location!.ToString();
        var id = int.Parse(detailsUrl.Split('/').Last());

        var bad = "ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate\nNope,Boarding,Senior,Onshore,Internal,100\n";
        await PostCsvAsync(client, detailsUrl, $"/Admin/RateCards/Import/{id}", bad);
        Assert.Equal(0, await EntryCountAsync(id));

        var good = "ResourceType,BusinessUnit,Seniority,Location,ResourcingClass,HourlyRate\n" +
                   "Software Engineer,Boarding,Senior,Onshore,Internal,100\n" +
                   "Software Engineer,Boarding,Senior,Offshore,Vendor,60\n";
        await PostCsvAsync(client, detailsUrl, $"/Admin/RateCards/Import/{id}", good);
        Assert.Equal(2, await EntryCountAsync(id));

        var export = await client.GetAsync($"/Admin/RateCards/Export/{id}");
        export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("Software Engineer,Boarding,Senior,Offshore,Vendor,60", csv);
    }

    [Fact]
    public async Task Allocation_template_requires_lines_totalling_100_percent()
    {
        var client = factory.CreateClient(NoRedirect);
        int typeId;
        using (var scope = factory.Services.CreateScope())
        {
            typeId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ResourceTypes.Select(t => t.Id).FirstAsync();
        }

        var key = $"K{Guid.NewGuid():N}"[..6];
        var form = new Dictionary<string, string>
        {
            ["Method"] = "TShirt", ["SizeKey"] = key, ["Name"] = "Test template",
            ["Lines[0].PhaseName"] = "Build", ["Lines[0].ResourceTypeId"] = typeId.ToString(), ["Lines[0].Seniority"] = "Mid", ["Lines[0].Percent"] = "60",
            ["Lines[1].PhaseName"] = "Test", ["Lines[1].ResourceTypeId"] = typeId.ToString(), ["Lines[1].Seniority"] = "Mid", ["Lines[1].Percent"] = "30"
        };
        var rejected = await PostFormAsync(client, "/Admin/Sizing/CreateTemplate", "/Admin/Sizing/CreateTemplate", form);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Contains("must total 100%", await rejected.Content.ReadAsStringAsync());

        form["Lines[1].Percent"] = "40";
        var accepted = await PostFormAsync(client, "/Admin/Sizing/CreateTemplate", "/Admin/Sizing/CreateTemplate", form);
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);

        using var verify = factory.Services.CreateScope();
        var template = await verify.ServiceProvider.GetRequiredService<AppDbContext>().AllocationTemplates
            .Include(t => t.Lines).SingleAsync(t => t.SizeKey == key);
        Assert.Equal(100m, template.Lines.Sum(l => l.Percent));
    }

    private async Task<RateCardStatus> StatusAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RateCards.AsNoTracking().SingleAsync(c => c.Id == id)).Status;
    }

    private async Task<int> EntryCountAsync(int id)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RateCardEntries.CountAsync(e => e.RateCardId == id);
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
        var content = new MultipartFormDataContent
        {
            { new StringContent(await GetTokenAsync(client, tokenPage)), "__RequestVerificationToken" }
        };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "rates.csv");
        return await client.PostAsync(postUrl, content);
    }
}
