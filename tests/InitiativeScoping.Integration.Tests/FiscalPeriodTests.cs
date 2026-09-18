using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class FiscalPeriodTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Fiscal_year_start_and_capex_flags_flow_through_details_portfolio_baselines_scenarios_and_exports()
    {
        await using var factory = new WebAppFactory();
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Default is a calendar fiscal year; the Admin page shows the selector.
        var admin = await client.GetStringAsync("/Admin/WorkCalendar");
        Assert.Contains("Fiscal year starts in", admin);
        Assert.Contains("<option selected=\"selected\" value=\"1\">January</option>", admin);

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/WorkCalendar", "/Admin/WorkCalendar/Settings", new() { ["HoursPerDay"] = "8", ["FiscalYearStartMonth"] = "7" })).StatusCode);
        var invalid = await PostFormAsync(client, "/Admin/WorkCalendar", "/Admin/WorkCalendar/Settings", new() { ["HoursPerDay"] = "8", ["FiscalYearStartMonth"] = "13" });
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(7, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().WorkCalendarSettings.SingleAsync()).FiscalYearStartMonth);
        }

        // Plan: May–Aug 2026 phase straddling the July fiscal-year boundary; capex labor + opex non-labor.
        var id = await CreateInitiativeAsync(client, factory, $"Fiscal {tag}");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-05-01", ["PlannedEnd"] = "2026-08-31" });
        int phaseId, typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }

        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000 capex.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.InternalId.ToString(), ["Quantity"] = "2", ["EstimatedHours"] = "100",
            ["CapexPercent"] = "100"
        })).StatusCode);
        // 4 months x 250 = 1,000 opex (0% Capex is the default when the field is omitted).
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["Category"] = nameof(CostCategory.SoftwareLicense), ["Description"] = "IDE seats", ["BillingModel"] = nameof(BillingModel.Monthly),
            ["Quantity"] = "1", ["UnitCost"] = "250"
        })).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(100m, (await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id)).CapexPercent);
            Assert.Equal(0m, (await db.InitiativeNonLaborCosts.SingleAsync(c => c.InitiativeId == id)).CapexPercent);
        }

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(details));
        Assert.Contains("Cost by fiscal year and quarter", html);
        Assert.Contains("Fiscal year starts in July", html);
        Assert.Contains("data-fiscal-year=\"2026\" data-fiscal-quarter=\"\"", html);
        Assert.Contains("data-fiscal-year=\"2026\" data-fiscal-quarter=\"4\"", html);
        Assert.Contains("data-fiscal-year=\"2027\" data-fiscal-quarter=\"1\"", html);
        Assert.Contains(">FY2026<", html);
        Assert.Contains(">FY2027 Q1<", html);
        Assert.Contains("$24,000", html);
        Assert.Contains("$1,000", html);

        // Baseline v1 snapshots the percentages; scenarios clone them.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Scenarios", new() { ["Name"] = $"Lean {tag}" })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseline = await db.ForecastBaselines.Include(b => b.Lines).Include(b => b.NonLaborLines).SingleAsync(b => b.InitiativeId == id);
            Assert.Equal(100m, Assert.Single(baseline.Lines).CapexPercent);
            Assert.Equal(0m, Assert.Single(baseline.NonLaborLines).CapexPercent);

            var scenario = await db.Initiatives.Include(i => i.Allocations).Include(i => i.NonLaborCosts).SingleAsync(i => i.ScenarioOfId == id);
            Assert.Equal(100m, Assert.Single(scenario.Allocations).CapexPercent);
            Assert.Equal(0m, Assert.Single(scenario.NonLaborCosts).CapexPercent);
        }

        var portfolio = WebUtility.HtmlDecode(await client.GetStringAsync("/Portfolio"));
        Assert.Contains("Cost by fiscal year and quarter", portfolio);
        Assert.Contains("id=\"portfolio-fiscal-table\"", portfolio);
        Assert.Contains(">FY2027<", portfolio);

        var csv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("# By fiscal period", csv);
        Assert.Contains("Fiscal year,Quarter,Period,Start,End,Forecast hours,Forecast labor cost,Forecast non-labor cost,Forecast cost,Forecast capex,Forecast opex,Baseline cost,Baseline capex,Baseline opex", csv);
        Assert.Contains("2026,,FY2026,2025-07-01,2026-06-30,", csv);
        Assert.Contains("2027,1,FY2027 Q1,2026-07-01,2026-09-30,", csv);
        Assert.Contains("Forecast capex,24000", csv);
        Assert.Contains("Forecast opex,1000", csv);
        Assert.Contains(",120,24000,100,24000,0,", csv);

        var portfolioCsv = await client.GetStringAsync("/Portfolio/Export?format=csv");
        Assert.Contains("# By fiscal period", portfolioCsv);
        Assert.Contains("# Initiative by fiscal period", portfolioCsv);
        Assert.Contains($"{id},Fiscal {tag},2027,1,FY2027 Q1,", portfolioCsv);
    }

    [Fact]
    public async Task Labor_capex_defaults_are_admin_configurable_and_prefill_new_allocations_by_class()
    {
        await using var factory = new WebAppFactory();
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Out of the box: Internal 70 / Vendor 100, editable per class under Admin → Resourcing classes.
        var admin = await client.GetStringAsync("/Admin/ResourcingClasses");
        Assert.Contains("Default Capex %", admin);
        Assert.Contains("<td>Internal</td>", admin);
        Assert.Contains(">70%</td>", admin);
        Assert.Contains(">100%</td>", admin);
        Assert.DoesNotContain("InternalCapexPercent", await client.GetStringAsync("/Admin/WorkCalendar"));

        var id = await CreateInitiativeAsync(client, factory, $"Capex default {tag}");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-05-01", ["PlannedEnd"] = "2026-08-31" });
        var page = await client.GetStringAsync(details);
        Assert.Matches("id=\"capitalization\"[^>]*value=\"70\"", page);
        Assert.Contains("\"capex\":70", page);
        Assert.Contains("\"capex\":100", page);

        // Apply a template as vendor labor: every generated line starts at the vendor default.
        int vendorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Domain.Entities.Vendor { Name = $"Vendor {tag}" };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();
            vendorId = vendor.Id;
        }
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/ApplySize/{id}", new()
        {
            ["Method"] = nameof(SizingMethod.TShirt), ["SizeKey"] = "L", ["Location"] = "Onshore", ["ResourcingClassId"] = ResourcingClass.VendorId.ToString(),
            ["VendorId"] = vendorId.ToString(), ["Replace"] = "true"
        })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lines = await db.InitiativeAllocations.Where(a => a.InitiativeId == id).ToListAsync();
            Assert.NotEmpty(lines);
            Assert.All(lines, a => Assert.Equal(100m, a.CapexPercent));
        }

        // Changing the class defaults affects new lines only; the existing vendor lines keep 100.
        var internalEdit = $"/Admin/ResourcingClasses/Edit/{ResourcingClass.InternalId}";
        var vendorEdit = $"/Admin/ResourcingClasses/Edit/{ResourcingClass.VendorId}";
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, internalEdit, internalEdit,
            new() { ["Name"] = "Internal", ["IsVendor"] = "false", ["DefaultCapexPercent"] = "55.5", ["SortOrder"] = "1", ["IsActive"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, vendorEdit, vendorEdit,
            new() { ["Name"] = "Vendor", ["IsVendor"] = "true", ["DefaultCapexPercent"] = "80", ["SortOrder"] = "2", ["IsActive"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostFormAsync(client, internalEdit, internalEdit,
            new() { ["Name"] = "Internal", ["IsVendor"] = "false", ["DefaultCapexPercent"] = "101", ["SortOrder"] = "1", ["IsActive"] = "true" })).StatusCode);
        page = await client.GetStringAsync(details);
        Assert.Matches("id=\"capitalization\"[^>]*value=\"55.5\"", page);
        Assert.Contains("\"capex\":55.5", page);
        Assert.Contains("\"capex\":80", page);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(55.5m, (await db.ResourcingClasses.SingleAsync(c => c.Id == ResourcingClass.InternalId)).DefaultCapexPercent);
            Assert.Equal(80m, (await db.ResourcingClasses.SingleAsync(c => c.Id == ResourcingClass.VendorId)).DefaultCapexPercent);
            Assert.All(await db.InitiativeAllocations.Where(a => a.InitiativeId == id).ToListAsync(), a => Assert.Equal(100m, a.CapexPercent));
        }
    }

    private static async Task<int> CreateInitiativeAsync(HttpClient client, WebAppFactory factory, string name)
    {
        int buId;
        using (var scope = factory.Services.CreateScope())
        {
            buId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
        }

        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-05-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
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
