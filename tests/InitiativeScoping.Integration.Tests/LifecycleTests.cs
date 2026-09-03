using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

/// <summary>Dev user holds only InitiativeOwner (no Administrator).</summary>
public class OwnerOnlyFactory : WebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        for (var i = 0; i < 5; i++)
        {
            builder.UseSetting($"Auth:Dev:Roles:{i}", "InitiativeOwner");
        }
    }
}

public class LifecycleTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Activation_is_blocked_until_scope_is_complete_and_priced()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Activation guard");
        var details = $"/Initiatives/Details/{id}";

        var html = await client.GetStringAsync(details);
        Assert.Contains("Not ready to activate", html);
        Assert.Contains("At least one phase is required.", html);

        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        Assert.Contains("Cannot activate", await client.GetStringAsync(details));
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(factory, id));

        // Unpriced allocation (Offshore has no seeded rate) still blocks.
        await AddPhaseAndAllocationAsync(client, factory, id, location: "Offshore");
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        html = await client.GetStringAsync(details);
        Assert.Contains("no matching published rate", html);
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(factory, id));
    }

    [Fact]
    public async Task Activation_creates_baseline_v1_locks_scope_and_audits()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Activate me");
        var details = $"/Initiatives/Details/{id}";
        var (phaseId, typeId) = await AddPhaseAndAllocationAsync(client, factory, id, location: "Onshore");

        Assert.Contains("Ready to activate", await client.GetStringAsync(details));
        var activate = await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new() { ["reason"] = "Approved by steering" });
        Assert.Equal(HttpStatusCode.Redirect, activate.StatusCode);

        var html = await client.GetStringAsync(details);
        Assert.Contains("Baseline v1", html);
        Assert.Contains("Scope is locked", html);
        Assert.DoesNotContain($"/Initiatives/AddPhase/{id}", html);

        // Scope mutations are rejected while Active.
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Mid),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "5"
        });
        Assert.Contains("Scope is locked", await client.GetStringAsync(details));

        // Delete is refused for non-Draft.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new())).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initiative = await db.Initiatives.Include(i => i.Baselines).ThenInclude(b => b.Lines).Include(i => i.Allocations).SingleAsync(i => i.Id == id);
        Assert.Equal(InitiativeStatus.Active, initiative.Status);
        var baseline = Assert.Single(initiative.Baselines);
        Assert.Equal(1, baseline.Version);
        Assert.True(baseline.IsCurrent);
        Assert.Equal("Approved by steering", baseline.Reason);
        Assert.Equal("dev-user", baseline.SnapshotBy);
        Assert.Equal(200m, baseline.TotalHours);
        Assert.Equal(24_000m, baseline.TotalCost);
        var line = Assert.Single(baseline.Lines);
        Assert.Equal(120m, line.HourlyRate);
        Assert.Single(initiative.Allocations);
        Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString() && a.Action == "StatusChange"));
        Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString() && a.Action == "Baseline"));
    }

    [Fact]
    public async Task Rebaseline_request_approve_edit_finalize_produces_v2_and_keeps_v1()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Rebaseline flow");
        var details = $"/Initiatives/Details/{id}";
        var (phaseId, typeId) = await AddPhaseAndAllocationAsync(client, factory, id, location: "Onshore");
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());

        // Finalize without a request is refused.
        await PostFormAsync(client, details, $"/Initiatives/{id}/FinalizeRebaseline", new());
        Assert.Contains("No approved re-baseline", await client.GetStringAsync(details));

        // Reason is required.
        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "  " });
        Assert.Contains("reason is required", await client.GetStringAsync(details));

        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Added QA" });
        var html = await client.GetStringAsync(details);
        Assert.Contains("Re-baseline requested", html);
        Assert.Contains("awaiting Administrator approval", html);

        // Duplicate request refused; scope still locked while Pending.
        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Again" });
        Assert.Contains("already open", await client.GetStringAsync(details));
        Assert.Single(await RequestsAsync(factory, id));
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", NewAllocation(phaseId, typeId));
        Assert.Contains("Scope is locked", await client.GetStringAsync(details));

        // Pending queue lists it; admin approves.
        var requestId = (await RequestsAsync(factory, id)).Single().Id;
        Assert.Contains("Rebaseline flow", await client.GetStringAsync("/Rebaselines"));
        await PostFormAsync(client, details, $"/Initiatives/{id}/DecideRebaseline", new() { ["requestId"] = requestId.ToString(), ["approve"] = "true", ["note"] = "ok" });
        html = await client.GetStringAsync(details);
        Assert.Contains("scope is unlocked", html);
        Assert.Contains($"/Initiatives/AddAllocation/{id}", html);

        // Deciding twice is refused.
        await PostFormAsync(client, details, $"/Initiatives/{id}/DecideRebaseline", new() { ["requestId"] = requestId.ToString(), ["approve"] = "false" });
        Assert.Contains("already Approved", await client.GetStringAsync(details));

        // Scope change now allowed: add a second allocation (1 x 50h Senior internal @ 120 = 6,000).
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", NewAllocation(phaseId, typeId));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.Equal(2, await AllocationCountAsync(factory, id));

        // Complete is blocked while the re-baseline is open.
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Complete) });
        Assert.Contains("Finalize or withdraw", await client.GetStringAsync(details));
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(factory, id));

        var finalize = await PostFormAsync(client, details, $"/Initiatives/{id}/FinalizeRebaseline", new());
        Assert.Equal(HttpStatusCode.Redirect, finalize.StatusCode);
        html = await client.GetStringAsync(details);
        Assert.Contains("Baseline v2", html);
        Assert.Contains("Scope is locked", html);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var baselines = await db.ForecastBaselines.Where(b => b.InitiativeId == id).OrderBy(b => b.Version).ToListAsync();
        Assert.Equal(2, baselines.Count);
        Assert.False(baselines[0].IsCurrent);
        Assert.Equal(24_000m, baselines[0].TotalCost);
        Assert.True(baselines[1].IsCurrent);
        Assert.Equal(30_000m, baselines[1].TotalCost);
        Assert.Equal("Added QA", baselines[1].Reason);
        var request = await db.RebaselineRequests.SingleAsync(r => r.Id == requestId);
        Assert.Equal(RebaselineStatus.Completed, request.Status);
        Assert.Equal(baselines[1].Id, request.ResultingBaselineId);
        Assert.Equal("ok", request.DecisionNote);

        // Baselines page shows both versions with deltas vs. v1.
        var baselinesHtml = await client.GetStringAsync($"/Initiatives/{id}/Baselines");
        Assert.Contains("v1", baselinesHtml);
        Assert.Contains("v2", baselinesHtml);
        Assert.Contains("+$6,000", WebUtility.HtmlDecode(baselinesHtml));
        Assert.Contains("+50.0", WebUtility.HtmlDecode(baselinesHtml));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Initiatives/{id}/Baselines?version=9")).StatusCode);
    }

    [Fact]
    public async Task Owner_without_admin_role_cannot_approve_and_withdraw_relocks_scope()
    {
        await using var ownerFactory = new OwnerOnlyFactory();
        var client = ownerFactory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, ownerFactory, "Owner approval");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(client, ownerFactory, id, location: "Onshore");
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(ownerFactory, id));

        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Need change" });
        var requestId = (await RequestsAsync(ownerFactory, id)).Single().Id;
        var html = await client.GetStringAsync(details);
        Assert.DoesNotContain("DecideRebaseline", html);

        var decide = await PostFormAsync(client, details, $"/Initiatives/{id}/DecideRebaseline", new() { ["requestId"] = requestId.ToString(), ["approve"] = "true" });
        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Rebaselines")).StatusCode);

        await PostFormAsync(client, details, $"/Initiatives/{id}/WithdrawRebaseline", new());
        Assert.Equal(RebaselineStatus.Withdrawn, (await RequestsAsync(ownerFactory, id)).Single().Status);
        Assert.Contains("Request a re-baseline to change it", await client.GetStringAsync(details));
    }

    [Fact]
    public async Task Status_transitions_follow_lifecycle_and_are_audited()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Status flow");
        var details = $"/Initiatives/Details/{id}";

        // Draft -> OnHold is not allowed; Draft -> Active must go through Activate.
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.OnHold) });
        Assert.Contains("Cannot move from Draft to OnHold", await client.GetStringAsync(details));
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Active) });
        Assert.Contains("Use Activate", await client.GetStringAsync(details));
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(factory, id));

        await AddPhaseAndAllocationAsync(client, factory, id, location: "Onshore");
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());

        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.OnHold), ["note"] = "Budget freeze" });
        Assert.Equal(InitiativeStatus.OnHold, await StatusAsync(factory, id));
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Active) });
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(factory, id));
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Complete) });
        Assert.Equal(InitiativeStatus.Complete, await StatusAsync(factory, id));
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Active) });
        Assert.Equal(InitiativeStatus.Complete, await StatusAsync(factory, id));

        var audit = await client.GetStringAsync($"/Audit?entity=Initiative&entityId={id}&act=StatusChange");
        Assert.Contains("Budget freeze", audit);
        Assert.Contains("\"To\":\"Complete\"", audit.Replace("&quot;", "\""));
    }

    [Fact]
    public async Task Viewer_cannot_activate_or_request_rebaseline()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        int id;
        using (var scope = viewerFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var initiative = new Initiative
            {
                Name = "Viewer lifecycle", BusinessUnitId = (await db.BusinessUnits.FirstAsync()).Id, TargetStart = new DateOnly(2026, 1, 1),
                CreatedBy = "dev-user", CreatedAt = DateTimeOffset.UtcNow, Status = InitiativeStatus.Active,
                Members = [new InitiativeMember { UserId = "dev-user", Role = InitiativeMemberRole.Owner }]
            };
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
            id = initiative.Id;
        }

        var client = viewerFactory.CreateClient(NoRedirect);
        var details = await client.GetStringAsync($"/Initiatives/Details/{id}");
        Assert.DoesNotContain("RequestRebaseline", details);
        Assert.DoesNotContain("ChangeStatus", details);
        Assert.DoesNotContain("__RequestVerificationToken", details);

        // A viewer sees no forms (so no antiforgery token); a forged POST must not succeed either way.
        foreach (var (url, fields) in new[]
        {
            ($"/Initiatives/{id}/Activate", new Dictionary<string, string>()),
            ($"/Initiatives/{id}/RequestRebaseline", new Dictionary<string, string> { ["reason"] = "x" }),
            ($"/Initiatives/{id}/ChangeStatus", new Dictionary<string, string> { ["to"] = "OnHold" })
        })
        {
            var response = await client.PostAsync(url, new FormUrlEncodedContent(fields));
            Assert.True(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest, $"{url} returned {response.StatusCode}");
        }

        Assert.Equal(InitiativeStatus.Active, await StatusAsync(viewerFactory, id));
        Assert.Empty(await RequestsAsync(viewerFactory, id));
        (await client.GetAsync($"/Initiatives/{id}/Baselines")).EnsureSuccessStatusCode();
        (await client.GetAsync("/Audit")).EnsureSuccessStatusCode();
    }

    // ----- helpers -----

    private static Dictionary<string, string> NewAllocation(int phaseId, int typeId) => new()
    {
        ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
        ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "50"
    };

    private static async Task<(int PhaseId, int TypeId)> AddPhaseAndAllocationAsync(HttpClient client, WebAppFactory f, int id, string location)
    {
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000.
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = location, ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        return (phaseId, typeId);
    }

    private static async Task<int> CreateInitiativeAsync(HttpClient client, WebAppFactory f, string name)
    {
        int buId;
        using (var scope = f.Services.CreateScope())
        {
            buId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
        }

        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-02-02"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private static async Task<InitiativeStatus> StatusAsync(WebAppFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.AsNoTracking().SingleAsync(i => i.Id == id)).Status;
    }

    private static async Task<int> AllocationCountAsync(WebAppFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InitiativeAllocations.CountAsync(a => a.InitiativeId == id);
    }

    private static async Task<List<RebaselineRequest>> RequestsAsync(WebAppFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RebaselineRequests.AsNoTracking().Where(r => r.InitiativeId == id).ToListAsync();
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
