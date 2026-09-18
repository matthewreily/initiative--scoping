using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class ChangeRequestTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Change_request_is_approved_opens_rebaseline_and_is_implemented_by_the_next_baseline()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Change control");
        var details = $"/Initiatives/Details/{id}";
        var (phaseId, typeId) = await AddPhaseAndAllocationAsync(client, factory, id);

        // Drafts are edited directly; no change requests.
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests", NewRequest());
        Assert.Contains("apply to Active initiatives", await client.GetStringAsync(details));
        Assert.Empty(await RequestsAsync(factory, id));

        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        var html = await client.GetStringAsync(details);
        Assert.Contains("Raise a change request", html);
        Assert.Contains("id=\"pane-changes\"", html);

        // Validation: reason required; schedule change needs a proposed end.
        var missingReason = NewRequest();
        missingReason["Reason"] = " ";
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests", missingReason);
        Assert.Contains("reason are required", await client.GetStringAsync(details));
        var schedule = NewRequest();
        schedule["Type"] = nameof(ChangeRequestType.Schedule);
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests", schedule);
        Assert.Contains("needs a proposed target end", await client.GetStringAsync(details));
        Assert.Empty(await RequestsAsync(factory, id));

        // Raise CR-1 with an estimate; the before-snapshot is the live forecast (2 x 100h @ 120 = 24,000) against baseline v1.
        var raise = await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests", NewRequest());
        Assert.Equal(HttpStatusCode.Redirect, raise.StatusCode);
        Assert.EndsWith("#pane-changes", raise.Headers.Location!.ToString());
        var request = Assert.Single(await RequestsAsync(factory, id));
        Assert.Equal(1, request.Number);
        Assert.Equal("CR-1", request.Code);
        Assert.Equal(ChangeRequestStatus.Pending, request.Status);
        Assert.Equal(ChangeRequestType.Scope, request.Type);
        Assert.Equal(6_000m, request.EstimatedCostImpact);
        Assert.Equal(50m, request.EstimatedHoursImpact);
        Assert.Equal(1, request.BaselineVersionBefore);
        Assert.Equal(200m, request.ForecastHoursBefore);
        Assert.Equal(24_000m, request.ForecastCostBefore);
        Assert.Null(request.ForecastCostAfter);
        Assert.Equal("dev-user", request.RequestedBy);

        html = await client.GetStringAsync(details);
        Assert.Contains("CR-1", html);
        Assert.Contains("Add QA automation", html);
        Assert.Contains("Pending", html);
        Assert.Contains("raised; an Administrator must approve", html);

        // Scope stays locked while the change is only requested.
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", NewAllocation(phaseId, typeId));
        Assert.Contains("Scope is locked", await client.GetStringAsync(details));

        // Approvals queue lists it; Portfolio flags it; deciding an unknown request 404s.
        var queue = await client.GetStringAsync("/Approvals");
        Assert.Contains("Change requests", queue);
        Assert.Contains("CR-1", queue);
        Assert.Contains("Change control", queue);
        Assert.Contains("1 change request pending", await client.GetStringAsync("/Portfolio"));
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests/9999/Decide", new() { ["approve"] = "true" })).StatusCode);

        // Admin approves: request Approved, a re-baseline is opened as Approved and linked, scope unlocks.
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests/{request.Id}/Decide", new() { ["approve"] = "true", ["note"] = "Go ahead" });
        request = Assert.Single(await RequestsAsync(factory, id));
        Assert.Equal(ChangeRequestStatus.Approved, request.Status);
        Assert.Equal("dev-user", request.DecidedBy);
        Assert.Equal("Go ahead", request.DecisionNote);
        Assert.NotNull(request.RebaselineRequestId);
        var rebaseline = Assert.Single(await RebaselinesAsync(factory, id));
        Assert.Equal(RebaselineStatus.Approved, rebaseline.Status);
        Assert.Equal(request.RebaselineRequestId, rebaseline.Id);
        Assert.Contains("CR-1", rebaseline.Reason);
        html = await client.GetStringAsync(details);
        Assert.Contains("scope is unlocked", html);
        Assert.Contains("v2 pending", html);
        Assert.Contains("1 change request approved", await client.GetStringAsync("/Portfolio"));
        Assert.DoesNotContain("CR-1", await client.GetStringAsync("/Approvals"));

        // Deciding twice is refused.
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests/{request.Id}/Decide", new() { ["approve"] = "false" });
        Assert.Contains("already Approved", await client.GetStringAsync(details));
        Assert.Equal(ChangeRequestStatus.Approved, (await RequestsAsync(factory, id)).Single().Status);

        // A second request raised while the re-baseline is open is numbered CR-2 and joins the same re-baseline on approval.
        var second = NewRequest();
        second["Title"] = "Extend by a month";
        second["Type"] = nameof(ChangeRequestType.Schedule);
        second["ProposedTargetEnd"] = "2026-05-29";
        second["EstimatedCostImpact"] = "";
        second["EstimatedHoursImpact"] = "";
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests", second);
        var cr2 = (await RequestsAsync(factory, id)).Single(c => c.Number == 2);
        Assert.Equal("CR-2", cr2.Code);
        Assert.Equal(new DateOnly(2026, 5, 29), cr2.ProposedTargetEnd);
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeRequests/{cr2.Id}/Decide", new() { ["approve"] = "true" });
        cr2 = (await RequestsAsync(factory, id)).Single(c => c.Number == 2);
        Assert.Equal(rebaseline.Id, cr2.RebaselineRequestId);
        Assert.Single(await RebaselinesAsync(factory, id));

        // Owner makes the change (+1 x 50h @ 120 = 6,000) and finalizes: both requests Implemented with the realised impact.
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", NewAllocation(phaseId, typeId));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var finalize = await PostFormAsync(client, details, $"/Initiatives/{id}/FinalizeRebaseline", new());
        Assert.Equal(HttpStatusCode.Redirect, finalize.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var v2 = await db.ForecastBaselines.SingleAsync(b => b.InitiativeId == id && b.Version == 2);
        var requests = await db.ChangeRequests.Where(c => c.InitiativeId == id).OrderBy(c => c.Number).ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.All(requests, c =>
        {
            Assert.Equal(ChangeRequestStatus.Implemented, c.Status);
            Assert.Equal(v2.Id, c.ResultingBaselineId);
            Assert.Equal(250m, c.ForecastHoursAfter);
            Assert.Equal(30_000m, c.ForecastCostAfter);
            Assert.Equal(6_000m, c.ActualCostImpact);
            Assert.Equal(50m, c.ActualHoursImpact);
        });

        var audit = await db.AuditEvents.Where(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Equal(2, audit.Count(a => a == AuditActions.ChangeRequest));
        Assert.Equal(2, audit.Count(a => a == AuditActions.ChangeRequestDecision));
        Assert.Equal(2, audit.Count(a => a == AuditActions.ChangeRequestImplemented));
        Assert.Contains(AuditActions.RebaselineRequest, audit);
        Assert.Contains(AuditActions.RebaselineDecision, audit);

        html = await client.GetStringAsync(details);
        Assert.Contains("Implemented", html);
        Assert.Contains("+6,000", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("change request", await client.GetStringAsync("/Portfolio"));

        // Exports carry the log.
        var csv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("Change requests", csv);
        Assert.Contains("CR-1", csv);
        Assert.Contains("Implemented", csv);
        var portfolioCsv = await client.GetStringAsync("/Portfolio/Export?format=csv");
        Assert.Contains("Pending change requests", portfolioCsv);
        Assert.Contains("CR-2", portfolioCsv);
    }

    [Fact]
    public async Task Owner_cannot_decide_but_can_withdraw_and_rejection_leaves_scope_locked()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
        await using var ownerFactory = new SharedDbFactory { DbPath = dbPath, UserId = "owner-cr", Role = "User" };
        await using var adminFactory = new SharedDbFactory { DbPath = dbPath, UserId = "admin-cr", Role = "Admin" };
        var owner = ownerFactory.CreateClient(NoRedirect);
        var admin = adminFactory.CreateClient(NoRedirect);

        var id = await CreateInitiativeAsync(owner, ownerFactory, "Owner change");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(owner, ownerFactory, id);
        await PostFormAsync(admin, details, $"/Initiatives/{id}/Activate", new());

        await PostFormAsync(owner, details, $"/Initiatives/{id}/ChangeRequests", NewRequest());
        var request = Assert.Single(await RequestsAsync(ownerFactory, id));
        Assert.Equal("owner-cr", request.RequestedBy);
        var html = await owner.GetStringAsync(details);
        Assert.Contains("Withdraw", html);
        Assert.DoesNotContain($"/ChangeRequests/{request.Id}/Decide", html);

        // Owner cannot approve; Admin can see the queue and the decide form.
        var forged = await PostFormAsync(owner, details, $"/Initiatives/{id}/ChangeRequests/{request.Id}/Decide", new() { ["approve"] = "true" });
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/Approvals")).StatusCode);
        Assert.Contains($"/ChangeRequests/{request.Id}/Decide", await admin.GetStringAsync(details));

        // Owner withdraws.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/ChangeRequests/{request.Id}/Withdraw", new());
        request = Assert.Single(await RequestsAsync(ownerFactory, id));
        Assert.Equal(ChangeRequestStatus.Withdrawn, request.Status);
        Assert.Equal("owner-cr", request.DecidedBy);
        Assert.Empty(await RebaselinesAsync(ownerFactory, id));

        // Admin rejects the next one: no re-baseline, scope still locked, requester and note recorded.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/ChangeRequests", NewRequest());
        var cr2 = (await RequestsAsync(ownerFactory, id)).Single(c => c.Number == 2);
        await PostFormAsync(admin, details, $"/Initiatives/{id}/ChangeRequests/{cr2.Id}/Decide", new() { ["approve"] = "false", ["note"] = "Not funded" });
        cr2 = (await RequestsAsync(ownerFactory, id)).Single(c => c.Number == 2);
        Assert.Equal(ChangeRequestStatus.Rejected, cr2.Status);
        Assert.Equal("admin-cr", cr2.DecidedBy);
        Assert.Equal("Not funded", cr2.DecisionNote);
        Assert.Null(cr2.RebaselineRequestId);
        Assert.Empty(await RebaselinesAsync(ownerFactory, id));
        html = await owner.GetStringAsync(details);
        Assert.Contains("Rejected", html);
        Assert.Contains("Not funded", html);
        Assert.DoesNotContain($"/Initiatives/AddAllocation/{id}\"", html);

        // Withdrawing a decided request is refused.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/ChangeRequests/{cr2.Id}/Withdraw", new());
        Assert.Equal(ChangeRequestStatus.Rejected, (await RequestsAsync(ownerFactory, id)).Single(c => c.Number == 2).Status);
    }

    private static Dictionary<string, string> NewRequest() => new()
    {
        ["Type"] = nameof(ChangeRequestType.Scope),
        ["Title"] = "Add QA automation",
        ["Description"] = "Add an automation engineer for the Build phase.",
        ["Reason"] = "Manual regression is too slow.",
        ["EstimatedCostImpact"] = "6000",
        ["EstimatedHoursImpact"] = "50"
    };

    private static Dictionary<string, string> NewAllocation(int phaseId, int typeId) => new()
    {
        ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
        ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "1", ["EstimatedHours"] = "50"
    };

    private static async Task<(int PhaseId, int TypeId)> AddPhaseAndAllocationAsync(HttpClient client, WebApplicationFactory<Program> f, int id)
    {
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        return (phaseId, typeId);
    }

    private static async Task<int> CreateInitiativeAsync(HttpClient client, WebApplicationFactory<Program> f, string name)
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

    private static async Task<List<ChangeRequest>> RequestsAsync(WebApplicationFactory<Program> f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ChangeRequests.AsNoTracking().Where(r => r.InitiativeId == id).ToListAsync();
    }

    private static async Task<List<RebaselineRequest>> RebaselinesAsync(WebApplicationFactory<Program> f, int id)
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
