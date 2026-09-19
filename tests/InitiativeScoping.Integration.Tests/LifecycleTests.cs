using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

/// <summary>Dev user holds only the legacy InitiativeOwner Entra role, which maps to User (no Admin).</summary>
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

/// <summary>Dev user with a chosen Entra role on a caller-supplied SQLite file, so two users (e.g. Owner and Admin) can share one database.</summary>
public class SharedDbFactory : WebAppFactory
{
    public required string DbPath { get; init; }
    public required string UserId { get; init; }
    public required string Role { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.UseSetting("Auth:Dev:UserId", UserId);
        builder.UseSetting("Auth:Dev:DisplayName", $"{Role} {UserId}");
        builder.UseSetting("Auth:Dev:Email", $"{UserId}@example.com");
        for (var i = 0; i < 5; i++)
        {
            builder.UseSetting($"Auth:Dev:Roles:{i}", Role);
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

        // Unpriced allocation (phase starts before any published rate card is effective) still blocks.
        await AddPhaseAndAllocationAsync(client, factory, id, location: "Offshore", phaseYear: 2020);
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
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "2",
            ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "1", ["EstimatedHours"] = "5"
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
        Assert.Contains("awaiting Admin approval", html);

        // Duplicate request refused; scope still locked while Pending.
        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Again" });
        Assert.Contains("already open", await client.GetStringAsync(details));
        Assert.Single(await RequestsAsync(factory, id));
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", NewAllocation(phaseId, typeId));
        Assert.Contains("Scope is locked", await client.GetStringAsync(details));

        // Pending queue lists it; admin approves.
        var requestId = (await RequestsAsync(factory, id)).Single().Id;
        Assert.Contains("Rebaseline flow", await client.GetStringAsync("/Approvals"));
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
        await SetStatusAsync(ownerFactory, id, InitiativeStatus.Active);

        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Need change" });
        var requestId = (await RequestsAsync(ownerFactory, id)).Single().Id;
        var html = await client.GetStringAsync(details);
        Assert.DoesNotContain("DecideRebaseline", html);

        var decide = await PostFormAsync(client, details, $"/Initiatives/{id}/DecideRebaseline", new() { ["requestId"] = requestId.ToString(), ["approve"] = "true" });
        Assert.Equal(HttpStatusCode.Forbidden, decide.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Approvals")).StatusCode);

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
        Assert.DoesNotContain("/Activate", details);

        // A viewer sees no lifecycle forms; a forged POST must not succeed either way.
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

    [Fact]
    public async Task Cancelled_draft_reopens_as_Draft_and_can_be_deleted()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Cancelled draft");
        var details = $"/Initiatives/Details/{id}";

        // Draft can't be reopened; Cancelled shows Reopen + Delete instead of Change status.
        await PostFormAsync(client, details, $"/Initiatives/{id}/Reopen", new());
        Assert.Contains("Only Cancelled initiatives can be reopened", await client.GetStringAsync(details));

        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Cancelled) });
        var html = await client.GetStringAsync(details);
        Assert.Contains($"/Initiatives/{id}/Reopen", html);
        Assert.Contains("reopen as Draft", html);
        Assert.Contains($"/Initiatives/Delete/{id}", html);
        Assert.DoesNotContain($"/Initiatives/{id}/ChangeStatus", html);

        await PostFormAsync(client, details, $"/Initiatives/{id}/Reopen", new() { ["note"] = "Funding restored" });
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(factory, id));
        var audit = (await client.GetStringAsync($"/Audit?entity=Initiative&entityId={id}&act=StatusChange")).Replace("&quot;", "\"");
        Assert.Contains("Funding restored", audit);
        Assert.Contains("\"Reopened\":true", audit);

        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Cancelled) });
        var delete = await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal("/Initiatives", delete.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(details)).StatusCode);
        Assert.Contains("\"Status\":\"Cancelled\"", (await client.GetStringAsync($"/Audit?entity=Initiative&entityId={id}&act=Delete")).Replace("&quot;", "\""));
    }

    [Fact]
    public async Task Cancelled_baselined_initiative_reopens_On_hold_and_deletes_with_its_history_unless_it_has_actuals()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, factory, "Cancelled after baseline");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(client, factory, id, location: "Onshore");
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(factory, id));

        // Active cannot be deleted, even by a forged POST.
        await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new());
        Assert.Contains("Only Draft or Cancelled initiatives can be deleted", await client.GetStringAsync(details));

        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "scope grew" });
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Cancelled) });
        Assert.Contains("reopen as On hold", await client.GetStringAsync(details));

        await PostFormAsync(client, details, $"/Initiatives/{id}/Reopen", new());
        Assert.Equal(InitiativeStatus.OnHold, await StatusAsync(factory, id));
        Assert.Contains("baseline v1 kept", await client.GetStringAsync(details));
        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Active) });
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(factory, id));

        await PostFormAsync(client, details, $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Cancelled) });
        int adjustmentId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adjustment = new ActualAdjustment { InitiativeId = id, Hours = 1, Cost = 100, Reason = "late invoice", CreatedBy = "dev-user", CreatedAt = DateTimeOffset.UtcNow };
            db.ActualAdjustments.Add(adjustment);
            await db.SaveChangesAsync();
            adjustmentId = adjustment.Id;
        }

        await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new());
        Assert.Contains("has recorded actuals and cannot be deleted", await client.GetStringAsync(details));
        Assert.Equal(InitiativeStatus.Cancelled, await StatusAsync(factory, id));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ActualAdjustments.Remove(await db.ActualAdjustments.SingleAsync(a => a.Id == adjustmentId));
            await db.SaveChangesAsync();
        }

        var delete = await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(details)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.ForecastBaselines.AnyAsync(b => b.InitiativeId == id));
            Assert.False(await db.RebaselineRequests.AnyAsync(r => r.InitiativeId == id));
            Assert.False(await db.Phases.AnyAsync(p => p.InitiativeId == id));
        }
    }

    // ----- helpers -----

    [Fact]
    public async Task Owner_requests_activation_and_admin_approval_activates_with_emails_and_audit()
    {
        var sender = new RecordingEmailSender();
        var dbPath = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
        await using var ownerFactory = new SharedDbFactory { DbPath = dbPath, UserId = "owner-1", Role = "User" }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(sender)));
        await using var adminFactory = new SharedDbFactory { DbPath = dbPath, UserId = "admin-1", Role = "Admin" }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(sender)));
        var owner = ownerFactory.CreateClient(NoRedirect);
        var admin = adminFactory.CreateClient(NoRedirect);
        Assert.Contains("Signed in as", await admin.GetStringAsync("/")); // materialises the admin's account so it receives request mail

        var id = await CreateInitiativeAsync(owner, ownerFactory, "Needs approval");
        var details = $"/Initiatives/Details/{id}";

        // Not ready: no request button enabled, and a forged request is refused.
        var html = await owner.GetStringAsync(details);
        Assert.Contains("Not ready to activate", html);
        Assert.DoesNotContain($"/Initiatives/{id}/Activate\"", html);
        await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new());
        Assert.Empty(await ActivationRequestsAsync(ownerFactory, id));

        await AddPhaseAndAllocationAsync(owner, ownerFactory, id, location: "Onshore");
        html = await owner.GetStringAsync(details);
        Assert.Contains("Ready for activation: an Administrator approves", html);
        Assert.Contains("RequestActivation", html);

        // Owner cannot bypass approval.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/Activate", new());
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(ownerFactory, id));
        Assert.Contains("Activation needs Administrator approval", await owner.GetStringAsync(details));

        var request = await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new() { ["reason"] = "Steering signed off" });
        Assert.Equal(HttpStatusCode.Redirect, request.StatusCode);
        var pending = Assert.Single(await ActivationRequestsAsync(ownerFactory, id));
        Assert.Equal(ActivationRequestStatus.Pending, pending.Status);
        Assert.Equal("owner-1", pending.RequestedBy);
        var requestMail = Assert.Single(sender.Sent);
        Assert.Contains("admin-1@example.com", requestMail.To);
        Assert.DoesNotContain("owner-1@example.com", requestMail.To);
        Assert.Contains("Activation requested: Needs approval", requestMail.Subject);
        Assert.Contains("Steering signed off", requestMail.TextBody);
        Assert.Contains(details, requestMail.TextBody);

        // Duplicate request is refused; owner sees pending state with withdraw but no decision controls.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new());
        Assert.Single(await ActivationRequestsAsync(ownerFactory, id));
        html = await owner.GetStringAsync(details);
        Assert.Contains("Activation requested", html);
        Assert.Contains("WithdrawActivation", html);
        Assert.DoesNotContain("DecideActivation", html);
        var forged = await PostFormAsync(owner, details, $"/Initiatives/{id}/DecideActivation", new() { ["requestId"] = pending.Id.ToString(), ["approve"] = "true" });
        Assert.Equal(HttpStatusCode.Forbidden, forged.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/Approvals")).StatusCode);

        // Admin sees it in the queue and approves; approval activates and captures v1.
        var queue = await admin.GetStringAsync("/Approvals");
        Assert.Contains("Needs approval", queue);
        Assert.Contains("Steering signed off", queue);
        var approve = await PostFormAsync(admin, "/Approvals", $"/Initiatives/{id}/DecideActivation", new() { ["requestId"] = pending.Id.ToString(), ["approve"] = "true", ["note"] = "Go" });
        Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(adminFactory, id));
        var decided = Assert.Single(await ActivationRequestsAsync(adminFactory, id));
        Assert.Equal(ActivationRequestStatus.Approved, decided.Status);
        Assert.Equal("admin-1", decided.DecidedBy);
        Assert.Equal("Go", decided.DecisionNote);
        html = await admin.GetStringAsync(details);
        Assert.Contains("Baseline v1", html);
        Assert.Contains("Scope is locked", html);
        Assert.DoesNotContain("Needs approval", await admin.GetStringAsync("/Approvals"));

        Assert.Equal(2, sender.Sent.Count);
        var decisionMail = sender.Sent[1];
        Assert.Equal(["owner-1@example.com"], decisionMail.To);
        Assert.Contains("Activation approved", decisionMail.Subject);
        Assert.Contains("Go", decisionMail.TextBody);

        using var scope = adminFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditEvents.Where(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Contains(AuditActions.ActivationRequest, actions);
        Assert.Contains(AuditActions.ActivationDecision, actions);
        Assert.Contains(AuditActions.StatusChange, actions);
        Assert.Contains(AuditActions.Baseline, actions);
    }

    [Fact]
    public async Task Rejected_activation_stays_draft_and_owner_can_request_again_or_withdraw()
    {
        var sender = new RecordingEmailSender { IsEnabled = false };
        var dbPath = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
        await using var ownerFactory = new SharedDbFactory { DbPath = dbPath, UserId = "owner-2", Role = "User" }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(sender)));
        await using var adminFactory = new SharedDbFactory { DbPath = dbPath, UserId = "admin-2", Role = "Admin" }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(sender)));
        var owner = ownerFactory.CreateClient(NoRedirect);
        var admin = adminFactory.CreateClient(NoRedirect);

        var id = await CreateInitiativeAsync(owner, ownerFactory, "Rejected once");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(owner, ownerFactory, id, location: "Onshore");
        await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new() { ["reason"] = "First try" });
        var first = Assert.Single(await ActivationRequestsAsync(ownerFactory, id));

        var reject = await PostFormAsync(admin, details, $"/Initiatives/{id}/DecideActivation", new() { ["requestId"] = first.Id.ToString(), ["approve"] = "false", ["note"] = "Add QA" });
        Assert.Equal(HttpStatusCode.Redirect, reject.StatusCode);
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(adminFactory, id));
        Assert.Equal(ActivationRequestStatus.Rejected, Assert.Single(await ActivationRequestsAsync(adminFactory, id)).Status);
        Assert.Empty(sender.Sent); // e-mail disabled: decision still recorded, nothing sent

        var html = await owner.GetStringAsync(details);
        Assert.Contains("Last request rejected", html);
        Assert.Contains("Add QA", html);
        Assert.Contains("RequestActivation", html);

        // Deciding the same request twice is refused.
        await PostFormAsync(admin, details, $"/Initiatives/{id}/DecideActivation", new() { ["requestId"] = first.Id.ToString(), ["approve"] = "true" });
        Assert.Equal(InitiativeStatus.Draft, await StatusAsync(adminFactory, id));

        // Owner requests again, then withdraws.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new());
        Assert.Equal(2, (await ActivationRequestsAsync(ownerFactory, id)).Count);
        await PostFormAsync(owner, details, $"/Initiatives/{id}/WithdrawActivation", new());
        var statuses = (await ActivationRequestsAsync(ownerFactory, id)).OrderBy(r => r.Id).Select(r => r.Status).ToList();
        Assert.Equal([ActivationRequestStatus.Rejected, ActivationRequestStatus.Withdrawn], statuses);
        Assert.DoesNotContain("Rejected once", await admin.GetStringAsync("/Approvals"));

        // Admin still activates directly; a pending request (if any) is closed as approved on the way.
        await PostFormAsync(owner, details, $"/Initiatives/{id}/RequestActivation", new());
        var direct = await PostFormAsync(admin, details, $"/Initiatives/{id}/Activate", new());
        Assert.Equal(HttpStatusCode.Redirect, direct.StatusCode);
        Assert.Equal(InitiativeStatus.Active, await StatusAsync(adminFactory, id));
        Assert.Equal(ActivationRequestStatus.Approved, (await ActivationRequestsAsync(adminFactory, id)).OrderBy(r => r.Id).Last().Status);
    }

    private static Dictionary<string, string> NewAllocation(int phaseId, int typeId) => new()
    {
        ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
        ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "1", ["EstimatedHours"] = "50"
    };

    private static async Task<(int PhaseId, int TypeId)> AddPhaseAndAllocationAsync(HttpClient client, WebApplicationFactory<Program> f, int id, string location, int phaseYear = 2026)
    {
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = $"{phaseYear}-03-01", ["PlannedEnd"] = $"{phaseYear}-04-30" });
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000.
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = location, ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "2", ["EstimatedHours"] = "100"
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

    private static async Task<InitiativeStatus> StatusAsync(WebApplicationFactory<Program> f, int id)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.AsNoTracking().SingleAsync(i => i.Id == id)).Status;
    }

    private static async Task<int> AllocationCountAsync(WebApplicationFactory<Program> f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InitiativeAllocations.CountAsync(a => a.InitiativeId == id);
    }

    private static async Task SetStatusAsync(WebApplicationFactory<Program> f, int id, InitiativeStatus status)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Initiatives.SingleAsync(i => i.Id == id)).Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<List<ActivationRequest>> ActivationRequestsAsync(WebApplicationFactory<Program> f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActivationRequests.AsNoTracking().Where(r => r.InitiativeId == id).ToListAsync();
    }

    private static async Task<List<RebaselineRequest>> RequestsAsync(WebApplicationFactory<Program> f, int id)
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
