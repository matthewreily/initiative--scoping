using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class ResourcingClassCatalogTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Seeds_internal_and_vendor_with_stable_ids_and_capex_defaults()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var internalClass = await db.ResourcingClasses.SingleAsync(c => c.Id == ResourcingClass.InternalId);
        Assert.Equal(ResourcingClass.InternalName, internalClass.Name);
        Assert.False(internalClass.IsVendor);
        Assert.Equal(70m, internalClass.DefaultCapexPercent);

        var vendorClass = await db.ResourcingClasses.SingleAsync(c => c.Id == ResourcingClass.VendorId);
        Assert.Equal(ResourcingClass.VendorName, vendorClass.Name);
        Assert.True(vendorClass.IsVendor);
        Assert.Equal(100m, vendorClass.DefaultCapexPercent);

        Assert.True(ResourcingClass.NameMatches(ResourcingClass.InternalName, ResourcingClass.LegacyInternalName));
        Assert.True(ResourcingClass.NameMatches(ResourcingClass.InternalName, "internal"));
        Assert.False(ResourcingClass.NameMatches(ResourcingClass.VendorName, ResourcingClass.LegacyInternalName));
    }

    [Fact]
    public async Task Catalog_crud_is_audited_delete_is_guarded_and_vendor_flag_locks_once_referenced()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Offshore partner {Guid.NewGuid():N}"[..30];

        var created = await PostFormAsync(client, "/Admin/ResourcingClasses/Create", "/Admin/ResourcingClasses/Create",
            new() { ["Name"] = name, ["IsVendor"] = "true", ["DefaultCapexPercent"] = "85", ["SortOrder"] = "5", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var duplicate = await PostFormAsync(client, "/Admin/ResourcingClasses/Create", "/Admin/ResourcingClasses/Create",
            new() { ["Name"] = name.ToUpperInvariant(), ["DefaultCapexPercent"] = "0", ["SortOrder"] = "6", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        int classId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.ResourcingClasses.SingleAsync(c => c.Name == name);
            classId = cls.Id;
            Assert.True(cls.IsVendor);
            Assert.Equal(85m, cls.DefaultCapexPercent);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(ResourcingClass) && a.EntityId == classId.ToString() && a.Action == "Create"));
        }

        var index = await client.GetStringAsync("/Admin/ResourcingClasses");
        Assert.Contains(name, index);
        Assert.Contains(ResourcingClass.InternalName, index);
        Assert.Contains(ResourcingClass.VendorName, index);

        // New class is offered on the People form; the legacy Work calendar page no longer carries Capex settings.
        Assert.Contains(name, await client.GetStringAsync("/Admin/People/Create"));
        Assert.DoesNotContain("InternalCapexPercent", await client.GetStringAsync("/Admin/WorkCalendar"));

        var edited = await PostFormAsync(client, $"/Admin/ResourcingClasses/Edit/{classId}", $"/Admin/ResourcingClasses/Edit/{classId}",
            new() { ["Id"] = classId.ToString(), ["Name"] = name + " renamed", ["IsVendor"] = "true", ["DefaultCapexPercent"] = "90", ["SortOrder"] = "5", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        name += " renamed";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(90m, (await db.ResourcingClasses.SingleAsync(c => c.Id == classId)).DefaultCapexPercent);
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(ResourcingClass) && a.EntityId == classId.ToString() && a.Action == "Update"));

            var type = await db.ResourceTypes.FirstAsync();
            var card = await db.RateCards.FirstAsync();
            db.RateCardEntries.Add(new RateCardEntry
            {
                RateCardId = card.Id, ResourceTypeId = type.Id, SeniorityId = 3,
                Location = "Moon", ResourcingClassId = classId, HourlyRate = 1m
            });
            await db.SaveChangesAsync();
        }

        // Referenced: Vendor-backed cannot flip, delete is refused (single and bulk), deactivation still works.
        var flip = await PostFormAsync(client, $"/Admin/ResourcingClasses/Edit/{classId}", $"/Admin/ResourcingClasses/Edit/{classId}",
            new() { ["Id"] = classId.ToString(), ["Name"] = name, ["IsVendor"] = "false", ["DefaultCapexPercent"] = "90", ["SortOrder"] = "5", ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, flip.StatusCode);
        Assert.Contains("cannot change while", await flip.Content.ReadAsStringAsync());

        var guarded = await PostFormAsync(client, "/Admin/ResourcingClasses", $"/Admin/ResourcingClasses/Delete/{classId}", new());
        Assert.Equal(HttpStatusCode.Redirect, guarded.StatusCode);
        Assert.Contains("Deactivate it instead", await client.GetStringAsync("/Admin/ResourcingClasses"));

        var bulkGuarded = await PostFormAsync(client, "/Admin/ResourcingClasses", "/Admin/ResourcingClasses/BulkDelete", new() { ["ids"] = classId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, bulkGuarded.StatusCode);

        var deactivated = await PostFormAsync(client, "/Admin/ResourcingClasses", "/Admin/ResourcingClasses/BulkDeactivate", new() { ["ids"] = classId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, deactivated.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cls = await db.ResourcingClasses.SingleAsync(c => c.Id == classId);
            Assert.True(cls.IsVendor);
            Assert.False(cls.IsActive);

            // Inactive classes drop out of pick lists unless the record already uses them.
            Assert.DoesNotContain(await ResourcingClassCatalog.OptionsAsync(db, null, CancellationToken.None), o => o.Id == classId);
            Assert.Contains(await ResourcingClassCatalog.OptionsAsync(db, [classId], CancellationToken.None), o => o.Id == classId && o.Name.EndsWith("(inactive)"));

            db.RateCardEntries.RemoveRange(db.RateCardEntries.Where(e => e.ResourcingClassId == classId));
            await db.SaveChangesAsync();
        }

        var activated = await PostFormAsync(client, "/Admin/ResourcingClasses", "/Admin/ResourcingClasses/BulkActivate", new() { ["ids"] = classId.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, activated.StatusCode);

        var deleted = await PostFormAsync(client, "/Admin/ResourcingClasses", $"/Admin/ResourcingClasses/Delete/{classId}", new());
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Null(await db.ResourcingClasses.FindAsync(classId));
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(ResourcingClass) && a.EntityId == classId.ToString() && a.Action == "Delete"));
        }
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string pageUrl)
    {
        var html = await client.GetStringAsync(pageUrl);
        var match = TokenRegex.Match(html);
        Assert.True(match.Success, $"No antiforgery token found on {pageUrl}");
        return match.Groups[1].Value;
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await GetTokenAsync(client, tokenPage);
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
