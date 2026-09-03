using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class InitiativeTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Index_and_create_render()
    {
        var client = factory.CreateClient();
        (await client.GetAsync("/Initiatives")).EnsureSuccessStatusCode();
        (await client.GetAsync("/Initiatives/Create")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Viewer_cannot_create_but_can_view()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        var client = viewerFactory.CreateClient();
        (await client.GetAsync("/Initiatives")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Initiatives/Create")).StatusCode);
    }

    [Fact]
    public async Task Viewer_who_is_owner_member_still_cannot_edit()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        int id;
        using (var scope = viewerFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var initiative = new Initiative
            {
                Name = "Viewer owned", BusinessUnitId = (await db.BusinessUnits.FirstAsync()).Id, TargetStart = new DateOnly(2026, 1, 1),
                CreatedBy = "dev-user", CreatedAt = DateTimeOffset.UtcNow,
                Members = [new InitiativeMember { UserId = "dev-user", Role = InitiativeMemberRole.Owner }]
            };
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
            id = initiative.Id;
        }

        var client = viewerFactory.CreateClient(NoRedirect);
        var details = await client.GetAsync($"/Initiatives/Details/{id}");
        Assert.Equal(HttpStatusCode.OK, details.StatusCode);
        Assert.DoesNotContain("/Initiatives/AddPhase/", await details.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/Initiatives/Edit/{id}")).StatusCode);
    }

    [Fact]
    public async Task Create_adds_creator_as_owner_and_writes_audit()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Audit test");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initiative = await db.Initiatives.Include(i => i.Members).SingleAsync(i => i.Id == id);
        Assert.Equal("dev-user", initiative.CreatedBy);
        Assert.Contains(initiative.Members, m => m.UserId == "dev-user" && m.Role == InitiativeMemberRole.Owner);
        Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString() && a.Action == "Create"));
    }

    [Fact]
    public async Task Relative_sizing_requires_size_key()
    {
        var client = factory.CreateClient(NoRedirect);
        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "No size", ["BusinessUnitId"] = (await SeededBusinessUnitIdAsync()).ToString(),
            ["SizingMethod"] = nameof(SizingMethod.TShirt), ["TargetStart"] = "2026-01-05"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Size is required", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Phases_and_allocations_produce_priced_forecast()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Forecast test");
        var details = $"/Initiatives/Details/{id}";

        var addPhase = await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new()
        {
            ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30"
        });
        Assert.Equal(HttpStatusCode.Redirect, addPhase.StatusCode);

        var (phaseId, typeId) = await FirstPhaseAndTypeAsync(id, "Software Engineer");
        var addAllocation = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, addAllocation.StatusCode);

        var html = await client.GetStringAsync(details);
        // Seeded rate: Senior internal = 60 + 20*3 = 120/h; 2 x 100h x 120 = 24,000
        Assert.Contains("200.0", html);
        Assert.Contains("24,000", html);
        Assert.DoesNotContain("Unpriced", html);
    }

    [Fact]
    public async Task Unmatched_rate_is_flagged_unpriced()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Unpriced test");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        var (phaseId, typeId) = await FirstPhaseAndTypeAsync(id, "Software Engineer");
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Offshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "10"
        });

        var html = await client.GetStringAsync(details);
        Assert.Contains("Unpriced", html);
        Assert.Contains("Incomplete", html);
    }

    [Fact]
    public async Task Phase_date_change_records_history_and_phase_with_allocations_cannot_be_deleted()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "History test");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        var (phaseId, typeId) = await FirstPhaseAndTypeAsync(id, "QA Analyst");

        var edit = await PostFormAsync(client, $"/Initiatives/EditPhase/{phaseId}", $"/Initiatives/EditPhase/{phaseId}", new()
        {
            ["Name"] = "Build", ["PlannedStart"] = "2026-03-15", ["PlannedEnd"] = "2026-05-15", ["Reason"] = "Vendor delay"
        });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

        var invalid = await PostFormAsync(client, $"/Initiatives/EditPhase/{phaseId}", $"/Initiatives/EditPhase/{phaseId}", new()
        {
            ["Name"] = "Build", ["PlannedStart"] = "2026-06-01", ["PlannedEnd"] = "2026-05-01"
        });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("on or after", await invalid.Content.ReadAsStringAsync());

        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Mid),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.Vendor), ["Quantity"] = "1", ["EstimatedHours"] = "10"
        });
        await PostFormAsync(client, details, $"/Initiatives/DeletePhase/{phaseId}", new());

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phase = await db.Phases.Include(p => p.DateHistory).SingleAsync(p => p.Id == phaseId);
        var history = Assert.Single(phase.DateHistory);
        Assert.Equal(new DateOnly(2026, 3, 1), history.OldStart);
        Assert.Equal(new DateOnly(2026, 5, 15), history.NewEnd);
        Assert.Equal("Vendor delay", history.Reason);
        Assert.Equal("dev-user", history.ChangedBy);
    }

    [Fact]
    public async Task Apply_size_creates_template_phases_and_allocations_summing_to_conversion_hours()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Sizing test");
        var details = $"/Initiatives/Details/{id}";

        var response = await PostFormAsync(client, details, $"/Initiatives/ApplySize/{id}", new()
        {
            ["Method"] = nameof(SizingMethod.TShirt), ["SizeKey"] = "L", ["Location"] = "Onshore",
            ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Replace"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initiative = await db.Initiatives.Include(i => i.Phases).Include(i => i.Allocations).SingleAsync(i => i.Id == id);
        Assert.Equal(["Discovery", "Build", "Launch"], initiative.Phases.OrderBy(p => p.Sequence).Select(p => p.Name));
        Assert.Equal(6, initiative.Allocations.Count);
        Assert.Equal(480m, initiative.Allocations.Sum(a => a.Quantity * a.EstimatedHours));
        Assert.Equal(SizingMethod.TShirt, initiative.SizingMethod);
        Assert.Equal("L", initiative.SizeKey);

        var missing = await PostFormAsync(client, details, $"/Initiatives/ApplySize/{id}", new()
        {
            ["Method"] = nameof(SizingMethod.TShirt), ["SizeKey"] = "XXXL", ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte)
        });
        Assert.Equal(HttpStatusCode.Redirect, missing.StatusCode);
        Assert.Contains("No sizing conversion", await client.GetStringAsync(details));
    }

    [Fact]
    public async Task Last_owner_cannot_be_removed()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Members test");
        var details = $"/Initiatives/Details/{id}";

        await PostFormAsync(client, details, $"/Initiatives/RemoveMember/{id}", new() { ["userId"] = "dev-user" });
        Assert.Contains("at least one Owner", await client.GetStringAsync(details));

        await PostFormAsync(client, details, $"/Initiatives/AddMember/{id}", new() { ["UserId"] = "alice", ["Role"] = nameof(InitiativeMemberRole.Contributor) });
        Assert.Contains("alice", await client.GetStringAsync(details));
    }

    private async Task<int> CreateInitiativeAsync(HttpClient client, string name)
    {
        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = (await SeededBusinessUnitIdAsync()).ToString(),
            ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-02-02"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private async Task<int> SeededBusinessUnitIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
    }

    private async Task<(int PhaseId, int ResourceTypeId)> FirstPhaseAndTypeAsync(int initiativeId, string typeName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == initiativeId)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == typeName)).Id;
        return (phaseId, typeId);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(tokenPage);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {tokenPage}");
        fields["__RequestVerificationToken"] = match.Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
