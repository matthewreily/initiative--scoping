using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class MultiBusinessUnitVendorTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Vendor_catalog_crud_is_audited_and_delete_is_guarded_by_references()
    {
        var client = factory.CreateClient(NoRedirect);
        var name = $"Acme {Guid.NewGuid():N}";

        var created = await PostFormAsync(client, "/Admin/Vendors/Create", "/Admin/Vendors/Create", new() { ["Name"] = name, ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);

        var duplicate = await PostFormAsync(client, "/Admin/Vendors/Create", "/Admin/Vendors/Create", new() { ["Name"] = name.ToUpperInvariant(), ["IsActive"] = "true" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        int vendorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var vendor = await db.Vendors.SingleAsync(v => v.Name == name);
            vendorId = vendor.Id;
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Vendor) && a.EntityId == vendorId.ToString() && a.Action == "Create"));

            var type = await db.ResourceTypes.FirstAsync();
            var card = await db.RateCards.FirstAsync(c => c.Status == RateCardStatus.Published);
            db.RateCardEntries.Add(new RateCardEntry
            {
                RateCardId = card.Id, ResourceTypeId = type.Id, SeniorityId = 1,
                Location = "Nearshore", ResourcingClass = ResourcingClass.Vendor, VendorId = vendorId, HourlyRate = 55m
            });
            await db.SaveChangesAsync();
        }

        var guarded = await PostFormAsync(client, "/Admin/Vendors/Create", $"/Admin/Vendors/Delete/{vendorId}", new());
        Assert.Equal(HttpStatusCode.Redirect, guarded.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(await db.Vendors.FindAsync(vendorId));
        }

        Assert.Contains(name, await client.GetStringAsync("/Admin/Vendors"));
    }

    [Fact]
    public async Task Initiative_can_span_business_units_and_vendors_with_global_pricing_and_rollups()
    {
        var client = factory.CreateClient(NoRedirect);
        var (sponsorId, partnerId, outsiderId, acmeId, globexId, typeId) = await SeedCatalogAsync();

        var create = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = $"Multi BU {Guid.NewGuid():N}", ["BusinessUnitId"] = sponsorId.ToString(),
            ["ParticipatingBusinessUnitIds"] = partnerId.ToString(),
            ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-03-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var id = int.Parse(DetailsRegex.Match(create.Headers.Location!.ToString()).Groups[1].Value);
        var details = $"/Initiatives/Details/{id}";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var initiative = await db.Initiatives.Include(i => i.ParticipatingBusinessUnits).SingleAsync(i => i.Id == id);
            Assert.Equal([partnerId], initiative.ParticipatingBusinessUnits.Select(p => p.BusinessUnitId));
            Assert.Equal(new[] { sponsorId, partnerId }.OrderBy(x => x), initiative.ParticipatingBusinessUnitIds.OrderBy(x => x));
        }

        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-02", ["PlannedEnd"] = "2026-03-31" });
        int phaseId;
        using (var scope = factory.Services.CreateScope())
        {
            phaseId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Phases.FirstAsync(p => p.InitiativeId == id)).Id;
        }

        Dictionary<string, string> Allocation(int bu, ResourcingClass cls, int? vendor) => new()
        {
            ["PhaseId"] = phaseId.ToString(), ["BusinessUnitId"] = bu.ToString(), ["ResourceTypeId"] = typeId.ToString(),
            ["SeniorityId"] = "3", ["Location"] = "Onshore", ["ResourcingClass"] = cls.ToString(),
            ["VendorId"] = vendor?.ToString() ?? string.Empty, ["Quantity"] = "1", ["EstimatedHours"] = "10"
        };

        var outsider = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(outsiderId, ResourcingClass.InternalFte, null));
        Assert.Equal(HttpStatusCode.Redirect, outsider.StatusCode);
        Assert.Contains("participating business units", await client.GetStringAsync(details));

        var noVendor = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(sponsorId, ResourcingClass.Vendor, null));
        Assert.Equal(HttpStatusCode.Redirect, noVendor.StatusCode);
        Assert.Contains("Select a vendor", await client.GetStringAsync(details));

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(sponsorId, ResourcingClass.InternalFte, acmeId))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(partnerId, ResourcingClass.InternalFte, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(sponsorId, ResourcingClass.Vendor, acmeId))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", Allocation(sponsorId, ResourcingClass.Vendor, globexId))).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var initiative = await db.Initiatives
                .Include(i => i.ParticipatingBusinessUnits)
                .Include(i => i.Phases)
                .Include(i => i.Allocations).ThenInclude(a => a.BusinessUnit)
                .Include(i => i.Allocations).ThenInclude(a => a.Vendor)
                .Include(i => i.NonLaborCosts)
                .SingleAsync(i => i.Id == id);
            var cards = await db.RateCards.Include(c => c.Entries).Where(c => c.Status == RateCardStatus.Published).ToListAsync();

            Assert.Equal(4, initiative.Allocations.Count);
            Assert.All(initiative.Allocations.Where(a => a.ResourcingClass == ResourcingClass.InternalFte), a => Assert.Null(a.VendorId));

            var forecast = ForecastCalculator.Calculate(initiative, cards);
            var byKey = forecast.Lines.ToDictionary(l => (l.Allocation.BusinessUnitId, l.Allocation.ResourcingClass, l.Allocation.VendorId), l => l.HourlyRate);
            Assert.Equal(100m, byKey[(sponsorId, ResourcingClass.InternalFte, null)]);
            Assert.Equal(100m, byKey[(partnerId, ResourcingClass.InternalFte, null)]);
            Assert.Equal(120m, byKey[(sponsorId, ResourcingClass.Vendor, acmeId)]);
            Assert.Equal(150m, byKey[(sponsorId, ResourcingClass.Vendor, globexId)]);
            Assert.Equal(10m * (100m + 100m + 120m + 150m), forecast.LaborCost);
        }

        var html = await client.GetStringAsync(details);
        Assert.Contains("Acme", html);
        Assert.Contains("Globex", html);

        var portfolio = await client.GetStringAsync($"/Portfolio?businessUnitId={sponsorId}");
        Assert.Contains("Labor by resourcing business unit", portfolio);
        Assert.Contains("Labor by vendor", portfolio);
        Assert.Contains("Acme", portfolio);
        Assert.Contains("Globex", portfolio);

        var csv = await client.GetStringAsync($"/Initiatives/{id}/Export?format=csv");
        Assert.Contains("Acme", csv);
        Assert.Contains("Globex", csv);

        var portfolioCsv = await client.GetStringAsync($"/Portfolio/Export?format=csv&businessUnitId={sponsorId}");
        Assert.Contains("By resourcing business unit", portfolioCsv);
        Assert.Contains("By vendor", portfolioCsv);

        var shrink = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", new()
        {
            ["Name"] = "Multi BU shrink", ["BusinessUnitId"] = sponsorId.ToString(),
            ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-03-01"
        });
        Assert.Equal(HttpStatusCode.OK, shrink.StatusCode);
        Assert.Contains("allocation", await shrink.Content.ReadAsStringAsync());
    }

    private async Task<(int Sponsor, int Partner, int Outsider, int Acme, int Globex, int TypeId)> SeedCatalogAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sponsor = new BusinessUnit { Name = $"Sponsor {suffix}" };
        var partner = new BusinessUnit { Name = $"Partner {suffix}" };
        var outsider = new BusinessUnit { Name = $"Outsider {suffix}" };
        var acme = new Vendor { Name = $"Acme {suffix}" };
        var globex = new Vendor { Name = $"Globex {suffix}" };
        db.AddRange(sponsor, partner, outsider, acme, globex);
        var type = await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer");

        RateCardEntry Entry(ResourcingClass cls, Vendor? vendor, decimal rate) => new()
        {
            ResourceType = type, SeniorityId = 3, Location = "Onshore", ResourcingClass = cls, Vendor = vendor, HourlyRate = rate
        };

        db.RateCards.Add(new RateCard
        {
            Name = $"Multi {suffix}", EffectiveStart = new DateOnly(2026, 1, 1), Status = RateCardStatus.Published,
            Entries =
            [
                Entry(ResourcingClass.InternalFte, null, 100m),
                Entry(ResourcingClass.Vendor, null, 110m),
                Entry(ResourcingClass.Vendor, acme, 120m),
                Entry(ResourcingClass.Vendor, globex, 150m)
            ]
        });
        await db.SaveChangesAsync();
        return (sponsor.Id, partner.Id, outsider.Id, acme.Id, globex.Id, type.Id);
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
