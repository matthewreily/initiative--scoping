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
        Assert.Contains("By business unit", html);

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
        var row = Assert.Single(text.Split('\n'), l => l.StartsWith($"{id},Export {tag},"));
        Assert.StartsWith($"{id},Export {tag},Boarding,Active,2026-03-01,1,200,24000,24000,0,200,24000,5,500,-23500,-97.9,", row);
        Assert.EndsWith(",10,No,No,No", row.TrimEnd('\r'));
        Assert.Contains("ETC cost,EAC cost,Projected variance,Projected variance %", text);
        Assert.Contains("# By status", text);

        var xlsx = await client.GetAsync("/Portfolio/Export?format=XLSX");
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal(["By business unit", "By status", "Initiatives"], await SheetNamesAsync(xlsx));

        var initiative = await client.GetAsync($"/Initiatives/{id}/Export?format=xlsx");
        Assert.Equal(HttpStatusCode.OK, initiative.StatusCode);
        Assert.Contains($"initiative-{id}-Export-{tag}.xlsx", initiative.Content.Headers.ContentDisposition!.FileName);
        Assert.Contains("Adjustments", await SheetNamesAsync(initiative));

        var initiativeCsv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("# Summary", initiativeCsv);
        Assert.Contains("Baseline cost,24000", initiativeCsv);
        Assert.Contains("# Forecast", initiativeCsv);
        Assert.Contains("Software Engineer,Senior,Onshore,InternalFte,2,100,200,120,24000", initiativeCsv);
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
    public async Task Viewer_sees_portfolio_but_cannot_export()
    {
        await using var viewerFactory = new ViewerOnlyFactory();
        var client = viewerFactory.CreateClient(NoRedirect);

        var page = await client.GetAsync("/Portfolio");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.DoesNotContain("Export?format", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Portfolio/Export?format=csv")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Initiatives/1/Export?format=csv")).StatusCode);
    }

    private static async Task<List<string>> SheetNamesAsync(HttpResponseMessage response)
    {
        using var archive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        using var reader = new StreamReader(archive.GetEntry("xl/workbook.xml")!.Open());
        return Regex.Matches(await reader.ReadToEndAsync(), "<(?:x:)?sheet [^>]*name=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).OrderBy(n => n).ToList();
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
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
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
