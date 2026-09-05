using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class NonLaborCostTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Admin_catalog_crud_is_validated_audited_and_guards_referenced_items()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..6];

        var labor = await PostFormAsync(client, "/Admin/CostCatalog/Create", "/Admin/CostCatalog/Create", new()
        {
            ["Category"] = nameof(CostCategory.Labor), ["Name"] = $"Bad {tag}", ["BillingModel"] = nameof(BillingModel.Monthly), ["UnitCost"] = "10"
        });
        Assert.Equal(HttpStatusCode.OK, labor.StatusCode);
        Assert.Contains("Labor is priced from rate cards", await labor.Content.ReadAsStringAsync());

        var create = await PostFormAsync(client, "/Admin/CostCatalog/Create", "/Admin/CostCatalog/Create", new()
        {
            ["Category"] = nameof(CostCategory.SoftwareLicense), ["Name"] = $"IDE {tag}", ["Vendor"] = "JetBrains",
            ["BillingModel"] = nameof(BillingModel.Monthly), ["UnitCost"] = "25", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var duplicate = await PostFormAsync(client, "/Admin/CostCatalog/Create", "/Admin/CostCatalog/Create", new()
        {
            ["Category"] = nameof(CostCategory.SoftwareLicense), ["Name"] = $"ide {tag}", ["BillingModel"] = nameof(BillingModel.Annual), ["UnitCost"] = "1"
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists in this category", await duplicate.Content.ReadAsStringAsync());

        int itemId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.CostCatalogItems.SingleAsync(i => i.Name == $"IDE {tag}");
            itemId = item.Id;
            Assert.Equal(25m, item.UnitCost);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(CostCatalogItem) && a.EntityId == itemId.ToString() && a.Action == "Create"));
        }

        var edit = await PostFormAsync(client, $"/Admin/CostCatalog/Edit/{itemId}", $"/Admin/CostCatalog/Edit/{itemId}", new()
        {
            ["Id"] = itemId.ToString(), ["Category"] = nameof(CostCategory.SoftwareLicense), ["Name"] = $"IDE {tag}", ["Vendor"] = "JetBrains",
            ["BillingModel"] = nameof(BillingModel.Annual), ["UnitCost"] = "250", ["IsActive"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        Assert.Contains($"IDE {tag}", await client.GetStringAsync("/Admin/CostCatalog"));

        // Reference the item from an initiative, then deletion must be refused.
        var id = await CreateInitiativeAsync(client, $"Catalog ref {tag}");
        var details = $"/Initiatives/Details/{id}";
        var add = await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["CostCatalogItemId"] = itemId.ToString(), ["Category"] = nameof(CostCategory.SoftwareLicense), ["Description"] = "IDE seats",
            ["BillingModel"] = nameof(BillingModel.Annual), ["Quantity"] = "3", ["UnitCost"] = "250"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        var delete = await PostFormAsync(client, "/Admin/CostCatalog/Create", $"/Admin/CostCatalog/Delete/{itemId}", new());
        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.Contains("cannot be deleted", await client.GetStringAsync("/Admin/CostCatalog"));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.CostCatalogItems.AnyAsync(i => i.Id == itemId));
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(CostCatalogItem) && a.EntityId == itemId.ToString() && a.Action == "Update"));
        }

        // Unreferenced items can be deleted.
        await PostFormAsync(client, "/Admin/CostCatalog/Create", "/Admin/CostCatalog/Create", new()
        {
            ["Category"] = nameof(CostCategory.Hardware), ["Name"] = $"Laptop {tag}", ["BillingModel"] = nameof(BillingModel.OneTime), ["UnitCost"] = "1500"
        });
        int laptopId;
        using (var scope = factory.Services.CreateScope())
        {
            laptopId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().CostCatalogItems.SingleAsync(i => i.Name == $"Laptop {tag}")).Id;
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/CostCatalog", $"/Admin/CostCatalog/Delete/{laptopId}", new())).StatusCode);
        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verifyDb.CostCatalogItems.AnyAsync(i => i.Id == laptopId));
        Assert.True(await verifyDb.AuditEvents.AnyAsync(a => a.Entity == nameof(CostCatalogItem) && a.EntityId == laptopId.ToString() && a.Action == "Delete"));
    }

    [Fact]
    public async Task Initiative_lines_are_validated_priced_by_whole_periods_and_shown_in_forecast()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "License lines");
        var details = $"/Initiatives/Details/{id}";
        var (phaseId, _) = await AddPhaseAndAllocationAsync(client, id);

        // Labor category is rejected.
        await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["Category"] = nameof(CostCategory.Labor), ["Description"] = "nope", ["BillingModel"] = nameof(BillingModel.Monthly), ["Quantity"] = "1", ["UnitCost"] = "1"
        });
        Assert.Contains("pick a non-labor category", await client.GetStringAsync(details));

        // Foreign phase is rejected.
        await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["PhaseId"] = "999999", ["Category"] = nameof(CostCategory.Cloud), ["Description"] = "nope", ["BillingModel"] = nameof(BillingModel.Monthly), ["Quantity"] = "1", ["UnitCost"] = "1"
        });
        Assert.Contains("belongs to this initiative", await client.GetStringAsync(details));

        // Half a date range is rejected.
        await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["Category"] = nameof(CostCategory.Cloud), ["Description"] = "nope", ["BillingModel"] = nameof(BillingModel.Monthly), ["Quantity"] = "1", ["UnitCost"] = "1", ["StartDate"] = "2026-03-01"
        });
        Assert.Contains("Enter both start and end dates", await client.GetStringAsync(details));

        // Phase-scoped monthly: Build is Mar 1 – Apr 30 = 2 periods × 4 seats × 25 = 200.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["Category"] = nameof(CostCategory.SoftwareLicense), ["Description"] = "IDE seats",
            ["BillingModel"] = nameof(BillingModel.Monthly), ["Quantity"] = "4", ["UnitCost"] = "25", ["ContractReference"] = "CTR-1"
        })).StatusCode);
        // Initiative-wide one-time hardware: 2 × 1,500 = 3,000.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["Category"] = nameof(CostCategory.Hardware), ["Description"] = "Laptops", ["BillingModel"] = nameof(BillingModel.OneTime), ["Quantity"] = "2", ["UnitCost"] = "1500"
        })).StatusCode);
        // Explicit dates spanning a partial month count as a whole extra month: Mar 15 – Apr 15 = 2 periods × 100 = 200.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["Category"] = nameof(CostCategory.Cloud), ["Description"] = "Sandbox", ["BillingModel"] = nameof(BillingModel.Monthly),
            ["Quantity"] = "1", ["UnitCost"] = "100", ["StartDate"] = "2026-03-15", ["EndDate"] = "2026-04-15"
        })).StatusCode);

        var html = await client.GetStringAsync(details);
        Assert.Contains("id=\"non-labor-costs\"", html);
        Assert.Contains("IDE seats", html);
        Assert.Contains("Whole initiative", html);
        Assert.Contains("Labor $24,000", html);
        Assert.Contains("Non-labor $3,400", html);
        Assert.Contains("$27,400", html);
        Assert.Contains("Ready to activate", html);

        int lineId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lines = await db.InitiativeNonLaborCosts.Where(l => l.InitiativeId == id).ToListAsync();
            Assert.Equal(3, lines.Count);
            lineId = lines.Single(l => l.Description == "Sandbox").Id;
            Assert.Equal("CTR-1", lines.Single(l => l.Description == "IDE seats").ContractReference);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(InitiativeNonLaborCost) && a.Action == "Create"));
        }

        // Edit page renders with the preview script; an inverted range is rejected server-side.
        var editPage = await client.GetAsync($"/Initiatives/EditNonLaborCost/{lineId}");
        Assert.Equal(HttpStatusCode.OK, editPage.StatusCode);
        var editHtml = await editPage.Content.ReadAsStringAsync();
        Assert.Contains("nl-preview", editHtml);
        Assert.Contains("value=\"2026-03-15\"", editHtml);

        var inverted = await PostFormAsync(client, $"/Initiatives/EditNonLaborCost/{lineId}", $"/Initiatives/EditNonLaborCost/{lineId}", new()
        {
            ["Category"] = nameof(CostCategory.Cloud), ["Description"] = "Sandbox", ["BillingModel"] = nameof(BillingModel.Monthly),
            ["Quantity"] = "1", ["UnitCost"] = "100", ["StartDate"] = "2026-04-15", ["EndDate"] = "2026-03-15"
        });
        Assert.Equal(HttpStatusCode.OK, inverted.StatusCode);
        Assert.Contains("on or after start", await inverted.Content.ReadAsStringAsync());

        var edited = await PostFormAsync(client, $"/Initiatives/EditNonLaborCost/{lineId}", $"/Initiatives/EditNonLaborCost/{lineId}", new()
        {
            ["Category"] = nameof(CostCategory.Cloud), ["Description"] = "Sandbox", ["BillingModel"] = nameof(BillingModel.Annual),
            ["Quantity"] = "1", ["UnitCost"] = "1000", ["StartDate"] = "2026-03-15", ["EndDate"] = "2026-04-15"
        });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        Assert.Contains("Non-labor $4,200", await client.GetStringAsync(details));

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/DeleteNonLaborCost/{lineId}", new())).StatusCode);
        Assert.Contains("Non-labor $3,200", await client.GetStringAsync(details));
    }

    [Fact]
    public async Task Empty_billing_window_blocks_activation_until_fixed()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(client, "Blocked by window");
        var details = $"/Initiatives/Details/{id}";
        await AddPhaseAndAllocationAsync(client, id);

        int lineId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var line = new InitiativeNonLaborCost
            {
                InitiativeId = id, Category = CostCategory.SoftwareLicense, Description = "Inverted", BillingModel = BillingModel.Monthly,
                Quantity = 1, UnitCost = 10m, StartDate = new DateOnly(2026, 5, 1), EndDate = new DateOnly(2026, 4, 1)
            };
            db.InitiativeNonLaborCosts.Add(line);
            await db.SaveChangesAsync();
            lineId = line.Id;
        }

        var html = await client.GetStringAsync(details);
        Assert.Contains("empty billing window", html);
        Assert.DoesNotContain("Ready to activate", html);
        await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new());
        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(InitiativeStatus.Draft, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.SingleAsync(i => i.Id == id)).Status);
        }

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/DeleteNonLaborCost/{lineId}", new())).StatusCode);
        Assert.Contains("Ready to activate", await client.GetStringAsync(details));
    }

    [Fact]
    public async Task Baseline_freezes_non_labor_lines_and_variance_exports_and_portfolio_include_them()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..6];
        var id = await CreateInitiativeAsync(client, $"Baseline NL {tag}");
        var details = $"/Initiatives/Details/{id}";
        var (phaseId, _) = await AddPhaseAndAllocationAsync(client, id);
        await PostFormAsync(client, details, $"/Initiatives/AddNonLaborCost/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["Category"] = nameof(CostCategory.SoftwareLicense), ["Description"] = "IDE seats",
            ["BillingModel"] = nameof(BillingModel.Monthly), ["Quantity"] = "4", ["UnitCost"] = "25"
        });

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", new())).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseline = await db.ForecastBaselines.Include(b => b.Lines).Include(b => b.NonLaborLines).SingleAsync(b => b.InitiativeId == id);
            Assert.Equal(200m, baseline.TotalHours);
            Assert.Equal(24_200m, baseline.TotalCost);
            var nl = Assert.Single(baseline.NonLaborLines);
            Assert.Equal(2, nl.Periods);
            Assert.Equal(200m, nl.Cost);
            Assert.Equal(phaseId, nl.PhaseId);
            Assert.Equal(new DateOnly(2026, 3, 1), nl.StartDate);

            // Scope is locked, so the line cannot be changed after activation.
            var lineId = (await db.InitiativeNonLaborCosts.SingleAsync(l => l.InitiativeId == id)).Id;
            await PostFormAsync(client, details, $"/Initiatives/DeleteNonLaborCost/{lineId}", new());
            Assert.True(await db.InitiativeNonLaborCosts.AnyAsync(l => l.Id == lineId));
        }

        var baselines = await client.GetStringAsync($"/Initiatives/{id}/Baselines");
        Assert.Contains("id=\"baseline-non-labor\"", baselines);
        Assert.Contains("IDE seats", baselines);
        Assert.Contains("Baseline total $24,200", baselines);

        // A software-license invoice lands as a categorised adjustment and shows up in the category variance.
        var actualsUrl = $"/Initiatives/{id}/Actuals";
        var adjust = await PostFormAsync(client, actualsUrl, $"/Initiatives/{id}/Adjustments", new()
        {
            ["Category"] = nameof(CostCategory.SoftwareLicense), ["Hours"] = "0", ["Cost"] = "150", ["Reason"] = "JetBrains invoice"
        });
        Assert.Equal(HttpStatusCode.Redirect, adjust.StatusCode);
        var actuals = await client.GetStringAsync(actualsUrl);
        Assert.Contains("By cost category", actuals);
        Assert.Contains("Software license", actuals);
        using (var scope = factory.Services.CreateScope())
        {
            var adjustment = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActualAdjustments.SingleAsync(a => a.InitiativeId == id);
            Assert.Equal(CostCategory.SoftwareLicense, adjustment.Category);
        }

        var export = await client.GetAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("Non-labor forecast", csv);
        Assert.Contains("Baseline non-labor", csv);
        Assert.Contains("IDE seats", csv);

        var portfolioCsv = await (await client.GetAsync("/Portfolio/Export?format=csv")).Content.ReadAsStringAsync();
        Assert.Contains("Non-labor forecast cost", portfolioCsv);
        var row = Assert.Single(portfolioCsv.Split('\n'), l => l.StartsWith($"{id},Baseline NL {tag},"));
        Assert.Contains(",200", row);
        Assert.Contains("Non-labor", await client.GetStringAsync("/Portfolio"));
    }

    [Fact]
    public async Task Details_lists_only_active_catalog_items_for_prefill()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..6];
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.CostCatalogItems.AddRange(
                new CostCatalogItem { Category = CostCategory.SoftwareLicense, Name = $"Active {tag}", UnitCost = 5m },
                new CostCatalogItem { Category = CostCategory.SoftwareLicense, Name = $"Retired {tag}", UnitCost = 5m, IsActive = false });
            await db.SaveChangesAsync();
        }

        var id = await CreateInitiativeAsync(client, $"Prefill {tag}");
        var html = await client.GetStringAsync($"/Initiatives/Details/{id}");
        Assert.Contains($"Active {tag}", html);
        Assert.DoesNotContain($"Retired {tag}", html);
    }

    private async Task<(int PhaseId, int TypeId)> AddPhaseAndAllocationAsync(HttpClient client, int id)
    {
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        // Seeded rate: Senior internal Onshore = 120/h; 2 x 100h = 24,000.
        var add = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        return (phaseId, typeId);
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
            ["Name"] = name, ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-02-02"
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
