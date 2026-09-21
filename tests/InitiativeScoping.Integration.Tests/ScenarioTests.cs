using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class ScenarioTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Clone_compare_and_promote_replace_the_live_plan_and_remove_the_scenario()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreatePlannedInitiativeAsync(client, $"Live {tag}");
        var details = $"/Initiatives/Details/{id}";

        var compare = WebUtility.HtmlDecode(await client.GetStringAsync($"/Initiatives/{id}/Scenarios"));
        Assert.Contains("No scenarios yet", compare);
        Assert.Contains("Live plan", compare);

        var create = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios", new() { ["Name"] = $"Lean {tag}" });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var scenarioId = int.Parse(DetailsRegex.Match(create.Headers.Location!.ToString()).Groups[1].Value);
        Assert.NotEqual(id, scenarioId);

        int scenarioPhaseId, liveAllocationId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var scenario = await db.Initiatives.Include(i => i.Phases).Include(i => i.Allocations).Include(i => i.NonLaborCosts).Include(i => i.Members)
                .SingleAsync(i => i.Id == scenarioId);
            Assert.Equal(id, scenario.ScenarioOfId);
            Assert.Equal(InitiativeStatus.Draft, scenario.Status);
            Assert.Single(scenario.Phases);
            Assert.Single(scenario.Allocations);
            Assert.Single(scenario.NonLaborCosts);
            Assert.NotEmpty(scenario.Members);
            scenarioPhaseId = scenario.Phases[0].Id;
            Assert.Equal(scenarioPhaseId, scenario.Allocations[0].PhaseId);
            Assert.Equal(scenarioPhaseId, scenario.NonLaborCosts[0].PhaseId);
            var livePhaseId = (await db.Phases.SingleAsync(p => p.InitiativeId == id)).Id;
            Assert.NotEqual(livePhaseId, scenarioPhaseId);
            liveAllocationId = (await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id)).Id;
        }

        // Scenario is hidden from the operational views and cannot be activated.
        var scenarioDetails = WebUtility.HtmlDecode(await client.GetStringAsync($"/Initiatives/Details/{scenarioId}"));
        Assert.Contains("What-if scenario", scenarioDetails);
        Assert.Contains($"Live {tag}", scenarioDetails);
        Assert.DoesNotContain($"Lean {tag}", WebUtility.HtmlDecode(await client.GetStringAsync("/Initiatives")));
        Assert.DoesNotContain($"Lean {tag}", WebUtility.HtmlDecode(await client.GetStringAsync("/Portfolio")));
        Assert.DoesNotContain($"Lean {tag}", await client.GetStringAsync("/Portfolio/Export?format=csv"));
        Assert.DoesNotContain($"/Initiatives/Details/{scenarioId}\"", await client.GetStringAsync($"/Search?q=Lean%20{tag}"));
        var activate = await PostFormAsync(client, $"/Initiatives/Details/{scenarioId}", $"/Initiatives/{scenarioId}/Activate", new());
        Assert.Equal(HttpStatusCode.Redirect, activate.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(InitiativeStatus.Draft, (await db.Initiatives.SingleAsync(i => i.Id == scenarioId)).Status);
            Assert.False(await db.ForecastBaselines.AnyAsync(b => b.InitiativeId == scenarioId));
        }

        // Scenarios stay Draft and carry no operational data: status changes, source mappings and adjustments are refused.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, $"/Initiatives/Details/{scenarioId}", $"/Initiatives/{scenarioId}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Cancelled) })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, $"/Initiatives/Details/{scenarioId}", $"/Initiatives/AddSourceMapping/{scenarioId}", new() { ["source"] = "Planview", ["externalProjectId"] = $"PV-{tag}" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, $"/Initiatives/Details/{scenarioId}", $"/Initiatives/{scenarioId}/Adjustments", new() { ["InitiativeId"] = scenarioId.ToString(), ["Hours"] = "5", ["Cost"] = "0", ["Reason"] = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios", new() { ["Name"] = new string('A', 301) })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(InitiativeStatus.Draft, (await db.Initiatives.SingleAsync(i => i.Id == scenarioId)).Status);
            Assert.False(await db.InitiativeSourceMappings.AnyAsync(m => m.InitiativeId == scenarioId));
            Assert.False(await db.ActualAdjustments.AnyAsync(a => a.InitiativeId == scenarioId));
            Assert.Equal(1, await db.Initiatives.CountAsync(i => i.ScenarioOfId == id));
        }

        // Edit the scenario only: 2 -> 1 person. Live plan stays 2 x 100h = 24,000.
        int typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var alloc = await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == scenarioId);
            typeId = alloc.ResourceTypeId;
            var edit = await PostFormAsync(client, $"/Initiatives/Details/{scenarioId}", $"/Initiatives/EditAllocation/{alloc.Id}", new()
            {
                ["Id"] = alloc.Id.ToString(), ["InitiativeId"] = scenarioId.ToString(), ["PhaseId"] = scenarioPhaseId.ToString(),
                ["BusinessUnitId"] = alloc.BusinessUnitId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
                ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "1", ["EstimatedHours"] = "100"
            });
            Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        }

        compare = WebUtility.HtmlDecode(await client.GetStringAsync($"/Initiatives/{id}/Scenarios"));
        Assert.Contains($"Lean {tag}", compare);
        Assert.Contains("$24,000", compare);
        Assert.Contains("$12,000", compare);
        Assert.Contains("-$12,000", compare);
        Assert.Contains("-100.0", compare);
        // Per-resource-type rows are grouped under the class they belong to: all hours here are internal, so the
        // type row follows "Internal hours" and the unused Vendor class gets no section at all.
        var internalIdx = compare.IndexOf("Internal hours", StringComparison.Ordinal);
        Assert.True(internalIdx > 0);
        Assert.DoesNotContain("Vendor hours", compare);
        Assert.Contains("compare-type-row", compare[internalIdx..]);
        Assert.Contains($"/Initiatives/{id}/Scenarios/{scenarioId}/Promote", compare);
        Assert.Contains($"/Initiatives/{id}/Scenarios/Print", compare);
        Assert.Contains($"Scenarios (1)", WebUtility.HtmlDecode(await client.GetStringAsync(details)));

        // Printable comparison: same figures, no app chrome, links or actions, plus each plan's phase schedule.
        foreach (var printUrl in new[] { $"/Initiatives/{id}/Scenarios/Print", $"/Initiatives/{scenarioId}/Scenarios/Print" })
        {
            var print = WebUtility.HtmlDecode(await client.GetStringAsync(printUrl));
            Assert.Contains("Scenario comparison", print);
            Assert.Contains($"Live {tag}", print);
            Assert.Contains($"Lean {tag}", print);
            Assert.Contains("$24,000", print);
            Assert.Contains("-$12,000", print);
            Assert.Contains("id=\"print-button\"", print);
            Assert.Contains("id=\"scenario-print-phases\"", print);
            Assert.DoesNotContain("/Promote", print);
            Assert.DoesNotContain("New scenario from live plan", print);
            Assert.DoesNotContain("class=\"navbar", print);
            Assert.DoesNotContain("/Initiatives/Details/", print);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Initiatives/999999/Scenarios/Print")).StatusCode);

        // Comparison export: same figures as data, reachable from the live plan or the scenario; the scenario itself exports like an initiative.
        Assert.Contains($"/Initiatives/{id}/Scenarios/Export?format=csv", compare);
        foreach (var exportUrl in new[] { $"/Initiatives/{id}/Scenarios/Export?format=csv", $"/Initiatives/{scenarioId}/Scenarios/Export?format=csv" })
        {
            var export = await client.GetAsync(exportUrl);
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
            Assert.Contains($"scenarios-{id}-", export.Content.Headers.ContentDisposition?.FileName);
            var csv = await export.Content.ReadAsStringAsync();
            Assert.Contains($"Live {tag} (live plan)", csv);
            Assert.Contains($"Lean {tag} vs live", csv);
            Assert.Contains("Cost,Forecast cost,24000", csv);
            Assert.Contains(",-12000", csv);
            Assert.Contains("# Phases", csv);
        }
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Initiatives/{id}/Scenarios/Export?format=xlsx")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Initiatives/{id}/Scenarios/Export?format=pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Initiatives/999999/Scenarios/Export?format=csv")).StatusCode);

        var scenarioPage = await client.GetStringAsync($"/Initiatives/Details/{scenarioId}");
        Assert.Contains($"/Initiatives/{scenarioId}/Export?format=csv", scenarioPage);
        var scenarioExport = await client.GetAsync($"/Initiatives/{scenarioId}/Export?format=csv");
        Assert.Equal(HttpStatusCode.OK, scenarioExport.StatusCode);
        Assert.Contains($"scenario-{scenarioId}-", scenarioExport.Content.Headers.ContentDisposition?.FileName);

        // Unrelated initiative cannot be promoted onto this one.
        var otherId = await CreatePlannedInitiativeAsync(client, $"Other {tag}");
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios/{otherId}/Promote", new())).StatusCode);

        var promote = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios/{scenarioId}/Promote", new());
        Assert.Equal(HttpStatusCode.Redirect, promote.StatusCode);
        Assert.EndsWith(details, promote.Headers.Location!.ToString());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var live = await db.Initiatives.Include(i => i.Phases).Include(i => i.Allocations).Include(i => i.NonLaborCosts).Include(i => i.Members).SingleAsync(i => i.Id == id);
            Assert.Equal($"Live {tag}", live.Name);
            Assert.Null(live.ScenarioOfId);
            Assert.Equal(InitiativeStatus.Draft, live.Status);
            var alloc = Assert.Single(live.Allocations);
            Assert.Equal(1, alloc.Quantity);
            Assert.NotEqual(liveAllocationId, alloc.Id);
            var phase = Assert.Single(live.Phases);
            Assert.Equal(phase.Id, alloc.PhaseId);
            Assert.Equal(phase.Id, Assert.Single(live.NonLaborCosts).PhaseId);
            Assert.NotEmpty(live.Members);

            Assert.False(await db.Initiatives.AnyAsync(i => i.Id == scenarioId));
            Assert.False(await db.Phases.AnyAsync(p => p.InitiativeId == scenarioId));
            Assert.False(await db.InitiativeAllocations.AnyAsync(a => a.InitiativeId == scenarioId));
            Assert.False(await db.InitiativeNonLaborCosts.AnyAsync(a => a.InitiativeId == scenarioId));
            Assert.True(await db.AuditEvents.AnyAsync(a => a.EntityId == id.ToString() && a.DiffJson!.Contains("PromoteScenario")));
        }

        var after = WebUtility.HtmlDecode(await client.GetStringAsync(details));
        Assert.Contains("promoted", after);
        Assert.Contains("Scenarios (0)", after);
        Assert.Contains("$12,000", after);
    }

    [Fact]
    public async Task Promotion_is_blocked_while_scope_is_locked_and_allowed_under_an_approved_rebaseline()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreatePlannedInitiativeAsync(client, $"Active {tag}");
        var details = $"/Initiatives/Details/{id}";
        var create = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios", new() { ["Name"] = $"Alt {tag}" });
        var scenarioId = int.Parse(DetailsRegex.Match(create.Headers.Location!.ToString()).Groups[1].Value);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new())).StatusCode);

        var compare = WebUtility.HtmlDecode(await client.GetStringAsync($"/Initiatives/{id}/Scenarios"));
        Assert.DoesNotContain($"/Scenarios/{scenarioId}/Promote", compare);
        Assert.Contains("approved re-baseline", compare);

        var blocked = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios/{scenarioId}/Promote", new());
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Initiatives.AnyAsync(i => i.Id == scenarioId));
            Assert.Equal(2, (await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id)).Quantity);
        }

        await PostFormAsync(client, details, $"/Initiatives/{id}/RequestRebaseline", new() { ["reason"] = "Scope change" });
        int requestId;
        using (var scope = factory.Services.CreateScope())
        {
            requestId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RebaselineRequests.SingleAsync(r => r.InitiativeId == id)).Id;
        }
        await PostFormAsync(client, details, $"/Initiatives/{id}/DecideRebaseline", new() { ["requestId"] = requestId.ToString(), ["approve"] = "true" });

        compare = WebUtility.HtmlDecode(await client.GetStringAsync($"/Initiatives/{id}/Scenarios"));
        Assert.Contains($"/Scenarios/{scenarioId}/Promote", compare);
        var promote = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios/{scenarioId}/Promote", new());
        Assert.Equal(HttpStatusCode.Redirect, promote.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Initiatives.AnyAsync(i => i.Id == scenarioId));
            var live = await db.Initiatives.Include(i => i.Baselines).Include(i => i.RebaselineRequests).SingleAsync(i => i.Id == id);
            Assert.Equal(InitiativeStatus.Active, live.Status);
            Assert.Single(live.Baselines);
            Assert.Equal(RebaselineStatus.Approved, live.RebaselineRequests.Single().Status);
        }
    }

    [Fact]
    public async Task Deleting_a_parent_removes_its_scenarios()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreatePlannedInitiativeAsync(client, $"Parent {tag}");
        var details = $"/Initiatives/Details/{id}";
        var create = await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios", new() { ["Name"] = $"Child {tag}" });
        var scenarioId = int.Parse(DetailsRegex.Match(create.Headers.Location!.ToString()).Groups[1].Value);

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/Delete/{id}", new())).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Initiatives.AnyAsync(i => i.Id == id || i.Id == scenarioId));
        Assert.False(await db.Phases.AnyAsync(p => p.InitiativeId == scenarioId));
    }

    private async Task<int> CreatePlannedInitiativeAsync(HttpClient client, string name)
    {
        int buId;
        using (var scope = factory.Services.CreateScope())
        {
            buId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
        }

        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-03-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var id = int.Parse(DetailsRegex.Match(response.Headers.Location!.ToString()).Groups[1].Value);
        var details = $"/Initiatives/Details/{id}";

        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        int phaseId, typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }

        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000.
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        var nonLabor = await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["Category"] = nameof(CostCategory.SoftwareLicense), ["Description"] = "License",
            ["BillingModel"] = nameof(BillingModel.OneTime), ["Quantity"] = "1", ["UnitCost"] = "0"
        });
        Assert.Equal(HttpStatusCode.Redirect, nonLabor.StatusCode);
        return id;
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
