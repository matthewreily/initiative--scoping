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

/// <summary>Multi-select bulk actions (save/adjust/delete selected) and the shared sortable/bulk table markup.</summary>
public class BulkActionsTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Rate_card_entries_bulk_save_adjust_and_delete_are_validated_and_audited()
    {
        var client = factory.CreateClient(NoRedirect);
        var (cardId, details) = await CreateDraftCardAsync(client);
        var (typeId, buId) = await LookupsAsync();
        int vendorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = new Vendor { Name = $"Vendor {Guid.NewGuid():N}"[..20], IsActive = true };
            db.Vendors.Add(vendor);
            await db.SaveChangesAsync();
            vendorId = vendor.Id;
        }

        foreach (var (seniority, cls, vendor) in new[] { ("Associate", "InternalFte", (int?)null), ("Mid", "InternalFte", null), ("Senior", "Vendor", vendorId) })
        {
            List<KeyValuePair<string, string>> fields =
            [
                new("ResourceTypeId", typeId.ToString()), new("BusinessUnitId", buId.ToString()), new("Seniority", seniority),
                new("Location", "Onshore"), new("ResourcingClass", cls), new("HourlyRate", "100")
            ];
            if (vendor is not null)
            {
                fields.Add(new("VendorId", vendor.Value.ToString()));
            }

            Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Admin/RateCards/AddEntry/{cardId}", fields)).StatusCode);
        }

        var ids = await EntryIdsAsync(cardId);
        Assert.Equal(3, ids.Count);
        var html = await client.GetStringAsync(details);
        Assert.Contains("data-sortable", html);
        Assert.Contains("data-bulk-all", html);
        Assert.Contains("BulkUpdateEntries", html);

        // Empty selection is rejected.
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkUpdateEntries/{cardId}", []);
        Assert.Contains("Select at least one entry", await client.GetStringAsync(details));

        // A negative rate anywhere in the selection rejects the whole batch (nothing saved).
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkUpdateEntries/{cardId}",
        [
            new("entryIds", ids[0].ToString()), new("entryIds", ids[1].ToString()),
            new($"rates[{ids[0]}]", "150"), new($"rates[{ids[1]}]", "-1")
        ]);
        Assert.Contains("between 0 and 100,000", await client.GetStringAsync(details));
        Assert.All(await RatesAsync(cardId), r => Assert.Equal(100m, r));

        // Save selected: only the selected/changed rows are updated and audited.
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkUpdateEntries/{cardId}",
        [
            new("entryIds", ids[0].ToString()), new("entryIds", ids[2].ToString()),
            new($"rates[{ids[0]}]", "150"), new($"rates[{ids[1]}]", "999"), new($"rates[{ids[2]}]", "100")
        ]);
        var rates = await RatesByIdAsync(cardId);
        Assert.Equal(150m, rates[ids[0]]);
        Assert.Equal(100m, rates[ids[1]]);
        Assert.Equal(100m, rates[ids[2]]);
        Assert.Equal(1, await AuditCountAsync(nameof(RateCardEntry), ids[0], AuditActions.Update));
        Assert.Equal(0, await AuditCountAsync(nameof(RateCardEntry), ids[2], AuditActions.Update));

        // Adjust %: applied to the selection with 2-dp rounding; -100% or worse is rejected.
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkAdjustEntries/{cardId}",
            [new("entryIds", ids[1].ToString()), new("entryIds", ids[2].ToString()), new("adjustPercent", "10.555")]);
        rates = await RatesByIdAsync(cardId);
        Assert.Equal(150m, rates[ids[0]]);
        Assert.Equal(110.56m, rates[ids[1]]);
        Assert.Equal(110.56m, rates[ids[2]]);
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkAdjustEntries/{cardId}", [new("entryIds", ids[1].ToString()), new("adjustPercent", "-100")]);
        Assert.Equal(110.56m, (await RatesByIdAsync(cardId))[ids[1]]);

        // Delete selected: one audit event per deleted row; the vendor entry survives untouched.
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkDeleteEntries/{cardId}",
            [new("entryIds", ids[0].ToString()), new("entryIds", ids[1].ToString()), new("entryIds", ids[1].ToString())]);
        var remaining = await EntryIdsAsync(cardId);
        Assert.Equal([ids[2]], remaining);
        Assert.Equal(1, await AuditCountAsync(nameof(RateCardEntry), ids[0], AuditActions.Delete));
        Assert.Equal(1, await AuditCountAsync(nameof(RateCardEntry), ids[1], AuditActions.Delete));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(vendorId, (await db.RateCardEntries.SingleAsync(e => e.Id == ids[2])).VendorId);
        }

        // Retired cards reject every bulk action.
        await PostFormAsync(client, details, $"/Admin/RateCards/Publish/{cardId}", []);
        await PostFormAsync(client, details, $"/Admin/RateCards/Retire/{cardId}", []);
        await PostFormAsync(client, details, $"/Admin/RateCards/BulkDeleteEntries/{cardId}", [new("entryIds", ids[2].ToString())]);
        Assert.Contains("Retired rate cards cannot be edited", await client.GetStringAsync(details));
        Assert.Equal([ids[2]], await EntryIdsAsync(cardId));
    }

    [Fact]
    public async Task Catalog_bulk_delete_skips_referenced_rows_and_bulk_deactivate_is_audited()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        int freeId, usedId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var free = new BusinessUnit { Name = $"Free {tag}", IsActive = true };
            var used = new BusinessUnit { Name = $"Used {tag}", IsActive = true };
            db.BusinessUnits.AddRange(free, used);
            await db.SaveChangesAsync();
            freeId = free.Id;
            usedId = used.Id;
        }

        var create = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create",
            [new("Name", $"Ref {tag}"), new("BusinessUnitId", usedId.ToString()), new("SizingMethod", nameof(SizingMethod.Direct)), new("TargetStart", "2026-02-02")]);
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);

        var index = await client.GetStringAsync("/Admin/BusinessUnits");
        Assert.Contains("data-bulk-all", index);
        Assert.Contains("BulkDeactivate", index);

        await PostFormAsync(client, "/Admin/BusinessUnits", "/Admin/BusinessUnits/BulkDeactivate", [new("ids", freeId.ToString()), new("ids", usedId.ToString())]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False((await db.BusinessUnits.SingleAsync(b => b.Id == freeId)).IsActive);
            Assert.False((await db.BusinessUnits.SingleAsync(b => b.Id == usedId)).IsActive);
        }

        Assert.Equal(1, await AuditCountAsync(nameof(BusinessUnit), usedId, AuditActions.Update));

        var response = await PostFormAsync(client, "/Admin/BusinessUnits", "/Admin/BusinessUnits/BulkDelete", [new("ids", freeId.ToString()), new("ids", usedId.ToString())]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var after = await client.GetStringAsync("/Admin/BusinessUnits");
        Assert.Contains($"Used {tag}", after);
        Assert.Contains("skipped", after);
        Assert.DoesNotContain($"Free {tag}", after);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.BusinessUnits.FindAsync(freeId));
            Assert.NotNull(await db.BusinessUnits.FindAsync(usedId));
        }

        Assert.Equal(1, await AuditCountAsync(nameof(BusinessUnit), freeId, AuditActions.Delete));
        Assert.Equal(0, await AuditCountAsync(nameof(BusinessUnit), usedId, AuditActions.Delete));

        // Empty selection is rejected with a message.
        await PostFormAsync(client, "/Admin/BusinessUnits", "/Admin/BusinessUnits/BulkDelete", []);
        Assert.Contains("Select at least one", await client.GetStringAsync("/Admin/BusinessUnits"));
    }

    [Fact]
    public async Task Initiative_bulk_delete_respects_scope_lock_and_only_touches_this_initiative()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var id = await CreateInitiativeAsync(client, $"Bulk {tag}");
        var other = await CreateInitiativeAsync(client, $"Other {tag}");
        var details = $"/Initiatives/Details/{id}";
        var (typeId, _) = await LookupsAsync();

        foreach (var (target, name) in new[] { (id, "Build"), (other, "Build") })
        {
            await PostFormAsync(client, $"/Initiatives/Details/{target}", $"/Initiatives/AddPhase/{target}", [new("Name", name), new("PlannedStart", "2026-03-01"), new("PlannedEnd", "2026-04-30")]);
        }

        int phaseId, otherPhaseId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            otherPhaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == other)).Id;
        }

        foreach (var (target, phase) in new[] { (id, phaseId), (id, phaseId), (other, otherPhaseId) })
        {
            var add = await PostFormAsync(client, $"/Initiatives/Details/{target}", $"/Initiatives/AddAllocation/{target}",
            [
                new("PhaseId", phase.ToString()), new("ResourceTypeId", typeId.ToString()), new("Seniority", nameof(Seniority.Senior)),
                new("Location", "Onshore"), new("ResourcingClass", nameof(ResourcingClass.InternalFte)), new("Quantity", "1"), new("EstimatedHours", "10")
            ]);
            Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
            var nl = await PostFormAsync(client, $"/Initiatives/Details/{target}", $"/Initiatives/AddNonLaborCost/{target}",
            [
                new("PhaseId", phase.ToString()), new("Category", nameof(CostCategory.Cloud)), new("Description", "Hosting"),
                new("BillingModel", nameof(BillingModel.Monthly)), new("Quantity", "1"), new("UnitCost", "100")
            ]);
            Assert.Equal(HttpStatusCode.Redirect, nl.StatusCode);
        }

        var html = await client.GetStringAsync(details);
        Assert.Contains("BulkDeleteAllocations", html);
        Assert.Contains("BulkDeleteNonLaborCosts", html);
        Assert.Contains("form=\"bulk-allocations\"", html);

        List<int> mine, theirs, myLines, theirLines;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            mine = await db.InitiativeAllocations.Where(a => a.InitiativeId == id).Select(a => a.Id).ToListAsync();
            theirs = await db.InitiativeAllocations.Where(a => a.InitiativeId == other).Select(a => a.Id).ToListAsync();
            myLines = await db.InitiativeNonLaborCosts.Where(a => a.InitiativeId == id).Select(a => a.Id).ToListAsync();
            theirLines = await db.InitiativeNonLaborCosts.Where(a => a.InitiativeId == other).Select(a => a.Id).ToListAsync();
        }

        Assert.Equal(2, mine.Count);

        // Ids from another initiative are ignored; own rows are removed and audited.
        await PostFormAsync(client, details, $"/Initiatives/BulkDeleteAllocations/{id}",
            [new("allocationIds", mine[0].ToString()), new("allocationIds", theirs[0].ToString())]);
        await PostFormAsync(client, details, $"/Initiatives/BulkDeleteNonLaborCosts/{id}",
            [new("lineIds", myLines[0].ToString()), new("lineIds", theirLines[0].ToString())]);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal([mine[1]], await db.InitiativeAllocations.Where(a => a.InitiativeId == id).Select(a => a.Id).ToListAsync());
            Assert.Equal(1, await db.InitiativeAllocations.CountAsync(a => a.InitiativeId == other));
            Assert.Equal([myLines[1]], await db.InitiativeNonLaborCosts.Where(a => a.InitiativeId == id).Select(a => a.Id).ToListAsync());
            Assert.Equal(1, await db.InitiativeNonLaborCosts.CountAsync(a => a.InitiativeId == other));
        }

        Assert.Equal(1, await AuditCountAsync(nameof(InitiativeAllocation), mine[0], AuditActions.Delete));
        Assert.Equal(1, await AuditCountAsync(nameof(InitiativeNonLaborCost), myLines[0], AuditActions.Delete));

        // Activation locks scope: bulk delete is refused and nothing changes.
        var activate = await PostFormAsync(client, details, $"/Initiatives/{id}/Activate", []);
        Assert.Equal(HttpStatusCode.Redirect, activate.StatusCode);
        await PostFormAsync(client, details, $"/Initiatives/BulkDeleteAllocations/{id}", [new("allocationIds", mine[1].ToString())]);
        Assert.Contains("Scope is locked", await client.GetStringAsync(details));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.InitiativeAllocations.CountAsync(a => a.InitiativeId == id));
        }
    }

    [Fact]
    public async Task Actuals_bulk_remap_assigns_selected_entries_and_reports_unmapped()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (typeId, buId) = await LookupsAsync();
        await PostFormAsync(client, "/Admin/People/Create", "/Admin/People/Create",
        [
            new("DisplayName", $"Sam {tag}"), new("ExternalIds", $"SAM-{tag}"), new("ResourceTypeId", typeId.ToString()), new("BusinessUnitId", buId.ToString()),
            new("Seniority", "Senior"), new("Location", "Onshore"), new("ResourcingClass", nameof(ResourcingClass.InternalFte)), new("IsActive", "true")
        ]);
        var id = await CreateInitiativeAsync(client, $"Remap {tag}");
        int personId, otherEntryId;
        List<int> entryIds;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            personId = (await db.People.SingleAsync(p => p.DisplayName == $"Sam {tag}")).Id;
            var import = new ActualsImport { Source = ActualsSources.Csv, FileName = $"{tag}.csv", StartedBy = "test", StartedAt = DateTimeOffset.UtcNow, Status = ActualsImportStatus.CompletedWithUnmapped };
            for (var i = 0; i < 3; i++)
            {
                import.Entries.Add(new ActualEntry { ExternalProjectId = $"PRJ-{tag}", ExternalPersonId = i == 2 ? "NOBODY" : $"SAM-{tag}", WorkDate = new DateOnly(2026, 3, 10 + i), Hours = 8, SourceReference = $"{tag}-{i}", IsUnmapped = true });
            }

            db.ActualsImports.Add(import);
            await db.SaveChangesAsync();
            entryIds = import.Entries.Select(e => e.Id).OrderBy(x => x).ToList();
            otherEntryId = entryIds[2];
        }

        var unmapped = await client.GetStringAsync("/Actuals/Unmapped");
        Assert.Contains("id=\"bulk-remap\"", unmapped);
        Assert.Contains("form=\"bulk-remap\"", unmapped);

        // Nothing chosen to assign -> rejected.
        await PostFormAsync(client, "/Actuals/Unmapped", "/Actuals/Entries/BulkRemap", [new("entryIds", entryIds[0].ToString()), new("returnUrl", "/Actuals/Unmapped")]);
        Assert.Contains("Choose an initiative and/or a person", await client.GetStringAsync("/Actuals/Unmapped"));

        // Assign initiative + person to two rows; the third (unknown person) gets only the initiative and stays unmapped.
        await PostFormAsync(client, "/Actuals/Unmapped", "/Actuals/Entries/BulkRemap",
            [new("entryIds", entryIds[0].ToString()), new("entryIds", entryIds[1].ToString()), new("initiativeId", id.ToString()), new("personId", personId.ToString()), new("returnUrl", "/Actuals/Unmapped")]);
        await PostFormAsync(client, "/Actuals/Unmapped", "/Actuals/Entries/BulkRemap",
            [new("entryIds", otherEntryId.ToString()), new("initiativeId", id.ToString()), new("returnUrl", "/Actuals/Unmapped")]);
        var page = await client.GetStringAsync("/Actuals/Unmapped");
        Assert.Contains("1 entry updated; 1 still unmapped", page);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.ActualEntries.Where(e => entryIds.Contains(e.Id)).OrderBy(e => e.Id).ToListAsync();
            Assert.All(rows, r => Assert.Equal(id, r.InitiativeId));
            Assert.Equal(personId, rows[0].PersonId);
            Assert.Equal(personId, rows[1].PersonId);
            Assert.False(rows[0].IsUnmapped);
            Assert.Equal(960m, rows[0].CalculatedCost); // Senior internal Onshore 120/h x 8h
            Assert.Null(rows[2].PersonId);
            Assert.True(rows[2].IsUnmapped);
        }

        Assert.Equal(1, await AuditCountAsync(nameof(ActualEntry), entryIds[0], AuditActions.Remap));
    }

    [Fact]
    public async Task List_tables_are_sortable_and_portfolio_has_no_bulk_controls()
    {
        var client = factory.CreateClient(NoRedirect);
        foreach (var path in new[] { "/Initiatives", "/Portfolio", "/Audit", "/Actuals", "/Admin/RateCards", "/Admin/People", "/Admin/Vendors", "/Admin/CostCatalog", "/Admin/WorkCalendar" })
        {
            var html = await client.GetStringAsync(path);
            Assert.Contains("data-sortable", html);
        }

        var portfolio = await client.GetStringAsync("/Portfolio");
        Assert.DoesNotContain("data-bulk", portfolio);
        var js = await client.GetStringAsync("/js/site.js");
        Assert.Contains("aria-sort", js);
        Assert.Contains("indeterminate", js);
    }

    // ----- helpers -----

    private async Task<(int CardId, string Details)> CreateDraftCardAsync(HttpClient client)
    {
        var create = await PostFormAsync(client, "/Admin/RateCards/Create", "/Admin/RateCards/Create",
            [new("Name", $"Bulk-{Guid.NewGuid():N}"[..14]), new("EffectiveStart", "2027-01-01")]);
        var details = create.Headers.Location!.ToString();
        return (int.Parse(details.Split('/').Last()), details);
    }

    private async Task<int> CreateInitiativeAsync(HttpClient client, string name)
    {
        int buId;
        using (var scope = factory.Services.CreateScope())
        {
            buId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
        }

        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create",
            [new("Name", name), new("BusinessUnitId", buId.ToString()), new("SizingMethod", nameof(SizingMethod.Direct)), new("TargetStart", "2026-02-02")]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private async Task<(int TypeId, int BuId)> LookupsAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return ((await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id, (await db.BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id);
    }

    private async Task<List<int>> EntryIdsAsync(int cardId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RateCardEntries.Where(e => e.RateCardId == cardId).OrderBy(e => e.Id).Select(e => e.Id).ToListAsync();
    }

    private async Task<List<decimal>> RatesAsync(int cardId) => (await RatesByIdAsync(cardId)).Values.ToList();

    private async Task<Dictionary<int, decimal>> RatesByIdAsync(int cardId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().RateCardEntries.Where(e => e.RateCardId == cardId).ToDictionaryAsync(e => e.Id, e => e.HourlyRate);
    }

    private async Task<int> AuditCountAsync(string entity, int entityId, string action)
    {
        using var scope = factory.Services.CreateScope();
        var key = entityId.ToString();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditEvents.CountAsync(a => a.Entity == entity && a.EntityId == key && a.Action == action);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, List<KeyValuePair<string, string>> fields)
    {
        var html = await client.GetStringAsync(tokenPage);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {tokenPage}");
        fields.Add(new("__RequestVerificationToken", match.Groups[1].Value));
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
