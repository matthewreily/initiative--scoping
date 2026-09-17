using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class PortfolioTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Portfolio_shows_rollups_threshold_flags_and_filters()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateActivatedInitiativeAsync(client, $"Portfolio {tag}");
        // Baseline v1 = 24,000; +30,000 adjustment => +25% > default 10%.
        await PostFormAsync(client, $"/Initiatives/{id}/Actuals", $"/Initiatives/{id}/Adjustments", new() { ["Hours"] = "0", ["Cost"] = "30000", ["Reason"] = "Invoice" });
        var draftId = await CreateInitiativeAsync(client, $"Draft {tag}");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Portfolio"));
        Assert.Contains($"Portfolio {tag}", html);
        Assert.Contains($"Draft {tag}", html);
        Assert.Contains("Over threshold", html);
        Assert.Contains("+25.0%", html);
        Assert.Contains("Export", html);
        Assert.Contains("By sponsor business unit", html);
        Assert.Contains("Labor by resourcing business unit", html);
        Assert.Contains("Labor by vendor", html);
        Assert.Contains("id=\"portfolio-phasing-chart\"", html);
        Assert.Contains("Cumulative forecast", html);

        var active = WebUtility.HtmlDecode(await client.GetStringAsync($"/Portfolio?status={nameof(InitiativeStatus.Active)}"));
        Assert.Contains($"Portfolio {tag}", active);
        Assert.DoesNotContain($"Draft {tag}", active);

        // Completed initiatives are hidden unless requested.
        await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Complete) });
        Assert.DoesNotContain($"Portfolio {tag}", await client.GetStringAsync("/Portfolio"));
        Assert.Contains($"Portfolio {tag}", await client.GetStringAsync("/Portfolio?includeClosed=true"));
        Assert.Contains($"Draft {tag}", await client.GetStringAsync("/Portfolio"));
        Assert.True(draftId > 0);
    }

    [Fact]
    public async Task Exports_portfolio_and_initiative_as_csv_and_xlsx_and_are_audited()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateActivatedInitiativeAsync(client, $"Export {tag}");
        await PostFormAsync(client, $"/Initiatives/{id}/Actuals", $"/Initiatives/{id}/Adjustments", new() { ["Hours"] = "5", ["Cost"] = "500", ["Reason"] = "Adj" });

        var csv = await client.GetAsync("/Portfolio/Export?format=csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.EndsWith(".csv", csv.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var text = await csv.Content.ReadAsStringAsync();
        Assert.StartsWith("# Initiatives", text);
        var initiativesSection = text[..text.IndexOf("\n# ", StringComparison.Ordinal)];
        var row = Assert.Single(initiativesSection.Split('\n'), l => l.StartsWith($"{id},Export {tag},"));
        Assert.StartsWith($"{id},Export {tag},Boarding,Active,2026-03-01,1,200,24000,24000,0,0,0,0,24000,,200,24000,5,500,-23500,-97.9,", row);
        Assert.EndsWith(",10,No,No,No,EffortDriven,", row.TrimEnd('\r'));
        Assert.Contains("ETC cost,EAC cost,Projected variance,Projected variance %", text);
        Assert.Contains("# By status", text);
        Assert.Contains("# By month", text);
        Assert.Contains("# Initiative by month", text);

        var xlsx = await client.GetAsync("/Portfolio/Export?format=XLSX");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["By month", "By resourcing business unit", "By sponsor business unit", "By status", "By vendor", "Initiative by month", "Initiatives"], await SheetNamesAsync(xlsx));

        var initiative = await client.GetAsync($"/Initiatives/{id}/Export?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, initiative.StatusCode);
        Assert.Contains($"initiative-{id}-Export-{tag}.xlsx", initiative.Content.Headers.ContentDisposition!.FileName);
        Assert.Contains("Adjustments", await SheetNamesAsync(initiative));

        var initiativeCsv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("# Summary", initiativeCsv);
        Assert.Contains("Baseline cost,24000", initiativeCsv);
        Assert.Contains("# Forecast", initiativeCsv);
        Assert.Contains("# By month", initiativeCsv);
        Assert.Contains("Month,Forecast hours,Forecast labor cost", initiativeCsv);
        Assert.Contains("Boarding,Software Engineer,Senior,Onshore,InternalFte,,2,100,200,120,24000", initiativeCsv);
        Assert.Contains("# Adjustments", initiativeCsv);
        Assert.Contains(",5,500,Adj", initiativeCsv);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Portfolio/Export?format=pdf")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/Initiatives/{id}/Export")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Initiatives/999999/Export?format=csv")).StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Entity == "Portfolio" && e.Action == "Export"));
        Assert.Equal(2, await db.AuditEvents.CountAsync(e => e.Entity == "Initiative" && e.EntityId == id.ToString() && e.Action == "Export"));
    }

    [Fact]
    public async Task Capacity_heatmap_shows_demand_by_resource_type_and_month_and_exports()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateActivatedInitiativeAsync(client, $"Capacity {tag}");

        // 2 x 100h over 1 Mar–30 Apr 2026 (61 days): Mar 101.64h, Apr 98.36h.
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Capacity"));
        Assert.Contains("id=\"capacity-heatmap\"", html);
        Assert.Contains("Software Engineer", html);
        Assert.Contains("Mar 26", html);
        Assert.Contains("Apr 26", html);
        Assert.Contains($"• Capacity {tag}: 102 h", html);
        Assert.Contains("id=\"capacity-over-count\"", html);

        var csv = await client.GetStringAsync("/Capacity/Export?format=csv");
        Assert.StartsWith("# Capacity", csv);
        Assert.Contains("Resource type,Month,Demand hours,Demand FTE,Headcount,Supply hours,Utilization,Over-allocated", csv);
        Assert.Contains("# Capacity by initiative", csv);
        Assert.Contains($",2026-03-01,{id},Capacity {tag},101.64", csv);
        Assert.Contains($",2026-04-01,{id},Capacity {tag},98.36", csv);

        var xlsx = await client.GetAsync("/Capacity/Export?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal(["Capacity", "Capacity by initiative"], await SheetNamesAsync(xlsx));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/Capacity/Export?format=pdf")).StatusCode);

        // Closed initiatives drop out of the heatmap unless requested.
        await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/{id}/ChangeStatus", new() { ["to"] = nameof(InitiativeStatus.Complete) });
        Assert.DoesNotContain($"Capacity {tag}", await client.GetStringAsync("/Capacity"));
        Assert.Contains($"Capacity {tag}", WebUtility.HtmlDecode(await client.GetStringAsync("/Capacity?includeClosed=true")));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.AuditEvents.AnyAsync(e => e.Entity == "Capacity" && e.Action == "Export"));
    }

    [Fact]
    public async Task Viewer_sees_portfolio_and_can_export_but_not_edit()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        var client = viewerFactory.CreateClient(NoRedirect);

        var page = await client.GetAsync("/Portfolio");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var csv = await client.GetAsync("/Portfolio/Export?format=csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Initiatives/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Users")).StatusCode);
    }

    private static async Task<List<string>> SheetNamesAsync(HttpResponseMessage response)
    {
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        using var reader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        return Regex.Matches(await reader.ReadToEndAsync(), "<(?:x:)?sheet [^>]*name=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).OrderBy(n => n).ToList();
    }

    [Fact]
    public async Task Contingency_and_confidence_flow_through_details_baseline_portfolio_and_exports()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateInitiativeAsync(client, $"Contingency {tag}");
        string name; int buId;
        using (var scope = factory.Services.CreateScope())
        {
            var i = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.SingleAsync(x => x.Id == id);
            name = i.Name; buId = i.BusinessUnitId;
        }

        var tooHigh = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", new()
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.EffortDriven), ["TargetStart"] = "2026-03-01", ["ContingencyPct"] = "150"
        });
        Assert.Equal(HttpStatusCode.OK, tooHigh.StatusCode);

        var edit = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", new()
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.EffortDriven), ["TargetStart"] = "2026-03-01",
            ["ContingencyPct"] = "10", ["EstimateConfidence"] = nameof(EstimateConfidence.Low)
        });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        int phaseId, typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(details));
        Assert.Contains("Contingency 10%", html);
        Assert.Contains("Low confidence", html);
        Assert.Contains("id=\"forecast-with-contingency\">$26,400", html);
        Assert.Contains("+$2,400 reserve (10%)", html);

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new())).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var baseline = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ForecastBaselines.SingleAsync(b => b.InitiativeId == id);
            Assert.Equal(24_000m, baseline.TotalCost);
            Assert.Equal(10m, baseline.ContingencyPct);
            Assert.Equal(2_400m, baseline.ContingencyCost);
            Assert.Equal(EstimateConfidence.Low, baseline.EstimateConfidence);
        }
        Assert.Contains("$2,400 (10%)", WebUtility.HtmlDecode(await client.GetStringAsync(details)));

        var portfolio = WebUtility.HtmlDecode(await client.GetStringAsync("/Portfolio"));
        Assert.Contains("id=\"portfolio-contingency\"", portfolio);
        Assert.Contains("10% · $26,400 total", portfolio);
        Assert.Contains("low-confidence", portfolio);

        var csv = await client.GetStringAsync("/Portfolio/Export?format=csv");
        Assert.Contains("Contingency %,Contingency cost,Forecast cost with contingency,Estimate confidence", csv);
        Assert.Contains($"Contingency {tag}", csv);
        Assert.Matches(new Regex($"Contingency {tag},.*,10(\\.0+)?,2400(\\.0+)?,26400(\\.0+)?,Low,"), csv);

        var initiativeCsv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("Contingency cost,2400", initiativeCsv);
        Assert.Contains("Estimate confidence,Low", initiativeCsv);
    }

    [Fact]
    public async Task Approved_budget_is_compared_against_forecast_on_details_portfolio_and_exports()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateInitiativeAsync(client, $"Budget {tag}");
        string name; int buId;
        using (var scope = factory.Services.CreateScope())
        {
            var i = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.SingleAsync(x => x.Id == id);
            name = i.Name; buId = i.BusinessUnitId;
        }

        var details = $"/Initiatives/Details/{id}";
        Assert.DoesNotContain("id=\"budget-card\"", await client.GetStringAsync(details));

        var negative = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", new()
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.EffortDriven), ["TargetStart"] = "2026-03-01", ["ApprovedBudget"] = "-5"
        });
        Assert.Equal(HttpStatusCode.OK, negative.StatusCode);

        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000 forecast against a 20,000 budget.
        var edit = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", new()
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.EffortDriven), ["TargetStart"] = "2026-03-01",
            ["ApprovedBudget"] = "20000", ["BudgetFiscalYear"] = " FY26 "
        });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);

        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        int phaseId, typeId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var i = await db.Initiatives.SingleAsync(x => x.Id == id);
            Assert.Equal(20_000m, i.ApprovedBudget);
            Assert.Equal("FY26", i.BudgetFiscalYear);
            var audits = await db.AuditEvents.Where(a => a.Entity == "Initiative" && a.EntityId == id.ToString() && a.Action == "Update").Select(a => a.DiffJson).ToListAsync();
            Assert.Contains(audits, d => d is not null && d.Contains("\"ApprovedBudget\":20000") && d.Contains("\"BudgetFiscalYear\":\"FY26\""));
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }
        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(details));
        Assert.Contains("Budget $20,000 (FY26)", html);
        Assert.Contains("id=\"approved-budget\">$20,000", html);
        Assert.Contains("−$4,000 over budget", html);
        Assert.Contains("id=\"budget-eac\">$24,000", html);
        Assert.Contains("Over budget", html);

        var portfolio = WebUtility.HtmlDecode(await client.GetStringAsync("/Portfolio"));
        Assert.Contains("id=\"portfolio-budget\"", portfolio);
        Assert.Contains("over budget", portfolio);
        Assert.Contains("120% used", portfolio);

        var csv = await client.GetStringAsync("/Portfolio/Export?format=csv");
        Assert.Contains("Approved budget,Budget fiscal year,Budget remaining,Budget used %,Over budget", csv);
        Assert.Matches(new Regex($"Budget {tag},.*,20000(\\.0+)?,FY26,-4000(\\.0+)?,120(\\.0+)?,Yes,"), csv);

        var initiativeCsv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("Approved budget,20000", initiativeCsv);
        Assert.Contains("Budget compared against,Forecast with contingency", initiativeCsv);
        Assert.Contains("Budget remaining,-4000", initiativeCsv);
    }

    private async Task<int> CreateActivatedInitiativeAsync(HttpClient client, string name)
    {
        var id = await CreateInitiativeAsync(client, name);
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
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        var activate = await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        Assert.Equal(HttpStatusCode.Redirect, activate.StatusCode);
        return id;
    }

    private async Task<int> CreateInitiativeAsync(HttpClient client, string name)
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
