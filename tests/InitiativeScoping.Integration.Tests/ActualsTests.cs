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

/// <summary>Dev user holds only FinancePmo.</summary>
public class FinanceOnlyFactory : WebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        for (var i = 0; i < 5; i++)
        {
            builder.UseSetting($"Auth:Dev:Roles:{i}", "FinancePmo");
        }
    }
}

public class ActualsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);
    private static readonly Regex ImportRegex = new("/Actuals/Imports/(\\d+)", RegexOptions.Compiled);
    private const string Header = "ExternalProjectId,ExternalPersonId,WorkDate,Hours,Cost,Reference\n";

    [Fact]
    public async Task People_roster_create_edit_duplicate_guard_and_delete_guard()
    {
        var client = factory.CreateClient(NoRedirect);
        var (typeId, buId) = await LookupsAsync(factory);
        var tag = Guid.NewGuid().ToString("N")[..8];

        var create = await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Jane {tag}", $"PV-{tag}; jane.{tag}@x.com, pv-{tag}", typeId, buId));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var personId = await PersonIdAsync(factory, $"Jane {tag}");
        Assert.Equal($"PV-{tag};jane.{tag}@x.com", await ExternalIdsAsync(factory, personId));

        // Same external id (case-variant) on another person is rejected.
        var dup = await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Dup {tag}", $"pv-{tag}", typeId, buId));
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Contains("already", await dup.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountPeopleAsync(factory, $"Dup {tag}"));

        // Search finds by external id.
        Assert.Contains($"Jane {tag}", await client.GetStringAsync($"/Admin/People?search=jane.{tag}"));

        // Import an actual for her, then delete is refused.
        var initiativeId = await CreateMappedInitiativeAsync(client, factory, $"Roster {tag}", $"PRJ-{tag}");
        await ImportAsync(client, Header + $"PRJ-{tag},PV-{tag},2026-03-10,8,,{tag}-1\n");
        var delete = await PostFormAsync(client, "/Admin/People", $"/Admin/People/Delete/{personId}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Equal(1, await CountPeopleAsync(factory, $"Jane {tag}"));
        Assert.Contains("Deactivate", await client.GetStringAsync("/Admin/People"));
        Assert.True(await HasActualsAsync(factory, initiativeId));
    }

    [Fact]
    public async Task Csv_import_maps_prices_skips_duplicates_and_rejects_invalid_files()
    {
        var client = factory.CreateClient(NoRedirect);
        var (typeId, buId) = await LookupsAsync(factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Sam {tag}", $"SAM-{tag}", typeId, buId, "Senior"));
        await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Off {tag}", $"OFF-{tag}", typeId, buId, "Senior", location: "Offshore"));
        await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Gone {tag}", $"GONE-{tag}", typeId, buId, "Senior", active: false));
        var id = await CreateMappedInitiativeAsync(client, factory, $"Import {tag}", $"PRJ-{tag}");

        // Invalid file: nothing is written.
        var bad = await ImportAsync(client, Header + $"PRJ-{tag},SAM-{tag},2026-13-01,8,,x\n", expectSuccess: false);
        Assert.Contains("Import rejected", await client.GetStringAsync("/Actuals"));
        Assert.Null(bad);

        var csv = Header +
            $"PRJ-{tag},SAM-{tag},2026-03-10,10,,{tag}-a\n" +           // priced: Senior internal Onshore = 120/h -> 1,200
            $"PRJ-{tag},sam-{tag},2026-03-11,5,999.99,{tag}-b\n" +     // sourced cost wins
            $"PRJ-{tag},OFF-{tag},2026-03-12,4,,{tag}-c\n" +           // mapped but unpriced (no Offshore rate)
            $"PRJ-{tag},GONE-{tag},2026-03-13,3,,{tag}-d\n" +          // inactive person -> unmapped
            $"PRJ-{tag},NOBODY,2026-03-14,2,,{tag}-e\n" +              // unknown person -> unmapped
            $"OTHER-{tag},SAM-{tag},2026-03-15,1,,{tag}-f\n";          // unknown project -> unmapped
        var importId = await ImportAsync(client, csv);
        Assert.NotNull(importId);

        var html = await client.GetStringAsync($"/Actuals/Imports/{importId}");
        Assert.Contains("CompletedWithUnmapped", html);
        Assert.Contains("Unpriced", html);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var import = await db.ActualsImports.Include(i => i.Entries).SingleAsync(i => i.Id == importId);
            Assert.Equal(6, import.RecordCount);
            Assert.Equal(3, import.UnmappedCount);
            Assert.Equal(0, import.SkippedCount);
            Assert.Equal("dev-user", import.StartedBy);
            Assert.Equal("actuals.csv", import.FileName);
            var a = import.Entries.Single(e => e.SourceReference == $"{tag}-a");
            Assert.Equal(1_200m, a.CalculatedCost);
            Assert.False(a.IsUnmapped);
            var b = import.Entries.Single(e => e.SourceReference == $"{tag}-b");
            Assert.Equal(999.99m, b.EffectiveCost);
            Assert.Equal(a.PersonId, b.PersonId);
            var c = import.Entries.Single(e => e.SourceReference == $"{tag}-c");
            Assert.False(c.IsUnmapped);
            Assert.Null(c.EffectiveCost);
            Assert.True(import.Entries.Single(e => e.SourceReference == $"{tag}-d").IsUnmapped);
            Assert.Null(import.Entries.Single(e => e.SourceReference == $"{tag}-d").PersonId);
            Assert.True(import.Entries.Single(e => e.SourceReference == $"{tag}-e").IsUnmapped);
            var f = import.Entries.Single(e => e.SourceReference == $"{tag}-f");
            Assert.True(f.IsUnmapped);
            Assert.Null(f.InitiativeId);
            Assert.NotNull(f.PersonId);
        }

        // Re-import the same file: everything skipped as duplicates.
        var second = await ImportAsync(client, csv);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var import = await db.ActualsImports.SingleAsync(i => i.Id == second);
            Assert.Equal(0, import.RecordCount);
            Assert.Equal(6, import.SkippedCount);
            Assert.Equal(6, await db.ActualEntries.CountAsync(e => e.SourceReference.StartsWith(tag)));
        }

        // Initiative variance excludes unmapped rows, counts sourced cost and flags the unpriced one.
        var actuals = await client.GetStringAsync($"/Initiatives/{id}/Actuals");
        Assert.Contains("19.0", actuals);            // 10 + 5 + 4 hours
        Assert.Contains("$2,200", actuals);          // 1,200 + 999.99 + 0
        Assert.Contains("1 mapped entry has no matching published rate", actuals);
        var details = await client.GetStringAsync($"/Initiatives/Details/{id}");
        Assert.Contains("2 imported entries for this initiative's projects await person mapping", WebUtility.HtmlDecode(details));
    }

    [Fact]
    public async Task Unmapped_review_assigns_and_reprices_and_reapply_uses_new_mappings()
    {
        var client = factory.CreateClient(NoRedirect);
        var (typeId, buId) = await LookupsAsync(factory);
        var tag = Guid.NewGuid().ToString("N")[..8];
        await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create", Person($"Ann {tag}", $"ANN-{tag}", typeId, buId, "Mid"));
        var personId = await PersonIdAsync(factory, $"Ann {tag}");
        var id = await CreateInitiativeAsync(client, factory, $"Unmapped {tag}");

        await ImportAsync(client, Header + $"NEW-{tag},ANN-{tag},2026-03-10,8,,{tag}-1\nNEW-{tag},WHO-{tag},2026-03-11,2,,{tag}-2\n");
        var unmapped = await client.GetStringAsync("/Actuals/Unmapped");
        Assert.Contains($"{tag}-1", unmapped);
        Assert.Contains($"{tag}-2", unmapped);

        int entry1, entry2;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            entry1 = (await db.ActualEntries.SingleAsync(e => e.SourceReference == $"{tag}-1")).Id;
            entry2 = (await db.ActualEntries.SingleAsync(e => e.SourceReference == $"{tag}-2")).Id;
        }

        // Empty assignment refused; assign initiative to entry 1 -> mapped and priced (Mid internal Onshore = 100/h).
        await PostFormAsync(client, "/Actuals/Unmapped", $"/Actuals/Entries/{entry1}/Remap", new());
        Assert.Contains("Choose an initiative", await client.GetStringAsync("/Actuals/Unmapped"));
        await PostFormAsync(client, "/Actuals/Unmapped", $"/Actuals/Entries/{entry1}/Remap", new() { ["initiativeId"] = id.ToString() });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.ActualEntries.SingleAsync(e => e.Id == entry1);
            Assert.False(e.IsUnmapped);
            Assert.Equal(id, e.InitiativeId);
            Assert.Equal(800m, e.CalculatedCost);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(ActualEntry) && a.EntityId == entry1.ToString() && a.Action == "Remap"));
        }

        // Add the project mapping and re-apply: entry 2 gets the initiative but stays unmapped (unknown person) until assigned.
        await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/AddSourceMapping/{id}", new() { ["source"] = "Csv", ["externalProjectId"] = $"new-{tag}" });
        await PostFormAsync(client, "/Actuals/Unmapped", "/Actuals/Unmapped/ApplyMappings", new());
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.ActualEntries.SingleAsync(e => e.Id == entry2);
            Assert.True(e.IsUnmapped);
            Assert.Equal(id, e.InitiativeId);
        }

        await PostFormAsync(client, "/Actuals/Unmapped", $"/Actuals/Entries/{entry2}/Remap", new() { ["personId"] = personId.ToString() });
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var e = await db.ActualEntries.SingleAsync(e => e.Id == entry2);
            Assert.False(e.IsUnmapped);
            Assert.Equal(200m, e.CalculatedCost);
        }

        Assert.Contains("10.0", await client.GetStringAsync($"/Initiatives/{id}/Actuals"));
    }

    [Fact]
    public async Task Adjustments_require_reason_and_feed_variance_against_current_baseline()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateInitiativeAsync(client, factory, $"Adjust {tag}");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(client, factory, id);
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        var actualsUrl = $"/Initiatives/{id}/Actuals";

        await PostFormAsync(client, actualsUrl, $"/Initiatives/{id}/Adjustments", new() { ["Hours"] = "10", ["Cost"] = "1000", ["Reason"] = " " });
        Assert.Contains("Reason", await client.GetStringAsync(actualsUrl));
        await PostFormAsync(client, actualsUrl, $"/Initiatives/{id}/Adjustments", new() { ["Hours"] = "0", ["Cost"] = "0", ["Reason"] = "noop" });
        Assert.Contains("must change hours or cost", await client.GetStringAsync(actualsUrl));

        // Baseline v1 = 24,000; +30,000 adjustment => +25% > default 10% threshold.
        var add = await PostFormAsync(client, actualsUrl, $"/Initiatives/{id}/Adjustments", new() { ["Hours"] = "0", ["Cost"] = "30000", ["Reason"] = "Vendor invoice INV-1" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(actualsUrl));
        Assert.Contains("Vendor invoice INV-1", html);
        Assert.Contains("+$6,000", html);
        Assert.Contains("+25.0%", html);
        Assert.Contains("Over threshold", html);
        Assert.Contains("exceeds the 10% threshold", WebUtility.HtmlDecode(await client.GetStringAsync(details)));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adjustment = await db.ActualAdjustments.SingleAsync(a => a.InitiativeId == id);
        Assert.Equal("dev-user", adjustment.CreatedBy);
        Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString() && a.Action == "Adjustment"));
        // Historical baseline is untouched by actuals.
        Assert.Equal(24_000m, (await db.ForecastBaselines.SingleAsync(b => b.InitiativeId == id)).TotalCost);
    }

    [Fact]
    public async Task Source_mappings_are_unique_case_insensitively_and_manager_only()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var a = await CreateInitiativeAsync(client, factory, $"Map A {tag}");
        var b = await CreateInitiativeAsync(client, factory, $"Map B {tag}");

        await PostFormAsync(client, $"/Initiatives/Details/{a}", $"/Initiatives/AddSourceMapping/{a}", new() { ["source"] = "Planview", ["externalProjectId"] = $"PV-{tag}" });
        Assert.Contains($"PV-{tag}", await client.GetStringAsync($"/Initiatives/Details/{a}"));
        await PostFormAsync(client, $"/Initiatives/Details/{b}", $"/Initiatives/AddSourceMapping/{b}", new() { ["source"] = "Planview", ["externalProjectId"] = $"pv-{tag}" });
        Assert.Contains("already mapped", await client.GetStringAsync($"/Initiatives/Details/{b}"));
        await PostFormAsync(client, $"/Initiatives/Details/{b}", $"/Initiatives/AddSourceMapping/{b}", new() { ["source"] = "Bogus", ["externalProjectId"] = "x" });
        Assert.Contains("Choose a source", await client.GetStringAsync($"/Initiatives/Details/{b}"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.InitiativeSourceMappings.CountAsync(m => m.ExternalProjectId.ToLower() == $"pv-{tag}"));
    }

    [Fact]
    public async Task Finance_can_import_but_not_admin_people_and_viewer_sees_nothing_writable()
    {
        await using var finance = new FinanceOnlyFactory();
        var fin = finance.CreateClient(NoRedirect);
        (await fin.GetAsync("/Actuals")).EnsureSuccessStatusCode();
        (await fin.GetAsync("/Actuals/Unmapped")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await fin.GetAsync("/Admin/People")).StatusCode);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var importId = await ImportAsync(fin, Header + $"X-{tag},Y,2026-03-10,1,,{tag}\n");
        Assert.NotNull(importId);
        var nav = await fin.GetStringAsync("/");
        Assert.Contains("/Actuals", nav);
        Assert.DoesNotContain("/Admin/", nav);

        await using var viewerFactory = new ViewerOnlyFactory();
        var viewer = viewerFactory.CreateClient(NoRedirect);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/Actuals")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/Actuals/Unmapped")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/Admin/People")).StatusCode);
        var status = (await viewer.PostAsync("/Initiatives/1/Adjustments", new FormUrlEncodedContent(new Dictionary<string, string> { ["Hours"] = "1", ["Cost"] = "1", ["Reason"] = "r" }))).StatusCode;
        Assert.True(status is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound, $"Unexpected {status}");
        Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync("/Initiatives/999999/Actuals")).StatusCode);
    }

    // ----- Helpers -----

    private static Dictionary<string, string> Person(string name, string ids, int typeId, int buId, string seniority = "Mid", string location = "Onshore", bool active = true) => new()
    {
        ["DisplayName"] = name, ["ExternalIds"] = ids, ["ResourceTypeId"] = typeId.ToString(), ["BusinessUnitId"] = buId.ToString(),
        ["Seniority"] = seniority, ["Location"] = location, ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["IsActive"] = active ? "true" : "false"
    };

    private static async Task<(int TypeId, int BuId)> LookupsAsync(WebAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return ((await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id, (await db.BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id);
    }

    private static async Task<int> PersonIdAsync(WebAppFactory f, string name)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().People.SingleAsync(p => p.DisplayName == name)).Id;
    }

    private static async Task<string?> ExternalIdsAsync(WebAppFactory f, int id)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().People.SingleAsync(p => p.Id == id)).ExternalIds;
    }

    private static async Task<int> CountPeopleAsync(WebAppFactory f, string name)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().People.CountAsync(p => p.DisplayName == name);
    }

    private static async Task<bool> HasActualsAsync(WebAppFactory f, int initiativeId)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActualEntries.AnyAsync(e => e.InitiativeId == initiativeId);
    }

    private static async Task<int> CreateMappedInitiativeAsync(HttpClient client, WebAppFactory f, string name, string projectId)
    {
        var id = await CreateInitiativeAsync(client, f, name);
        var map = await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/AddSourceMapping/{id}", new() { ["source"] = "Csv", ["externalProjectId"] = projectId });
        Assert.Equal(HttpStatusCode.Redirect, map.StatusCode);
        return id;
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
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-03-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private static async Task AddPhaseAndAllocationAsync(HttpClient client, WebAppFactory f, int id)
    {
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        int phaseId, typeId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }

        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000.
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
    }

    /// <summary>Uploads a CSV; returns the new import id, or null when the upload was rejected (redirect back to /Actuals).</summary>
    private static async Task<int?> ImportAsync(HttpClient client, string csv, bool expectSuccess = true)
    {
        var content = new MultipartFormDataContent { { new StringContent(await GetTokenAsync(client, "/Actuals")), "__RequestVerificationToken" } };
        var file = new StringContent(csv);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "actuals.csv");
        var response = await client.PostAsync("/Actuals/Import", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = ImportRegex.Match(response.Headers.Location!.ToString());
        Assert.Equal(expectSuccess, match.Success);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string page)
    {
        var html = await client.GetStringAsync(page);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {page}");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetTokenAsync(client, tokenPage);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
