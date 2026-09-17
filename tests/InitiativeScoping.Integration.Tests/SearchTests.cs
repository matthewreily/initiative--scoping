using System.Net;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class SearchTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    [Fact]
    public async Task Short_or_empty_query_shows_hint_without_results()
    {
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/Search");
        Assert.Contains("at least two characters", page);
        page = await client.GetStringAsync("/Search?q=a");
        Assert.Contains("at least two characters", page);
    }

    [Fact]
    public async Task Admin_sees_initiatives_people_rate_cards_and_vendors()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        int initiativeId, personId, rateCardId, vendorId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bu = await db.BusinessUnits.FirstAsync();
            var rt = await db.ResourceTypes.FirstAsync();
            var sen = await db.SeniorityLevels.FirstAsync();
            var initiative = new Initiative { Name = $"Zeta {tag}", BusinessUnit = bu, CreatedBy = "admin", TargetStart = new DateOnly(2027, 1, 1) };
            var person = new Person { DisplayName = $"Pat {tag}", ResourceType = rt, Seniority = sen, BusinessUnit = bu, ResourcingClass = ResourcingClass.InternalFte, Location = "Onshore" };
            var card = new RateCard { Name = $"Card {tag}", EffectiveStart = new DateOnly(2027, 1, 1) };
            var vendor = new Vendor { Name = $"Vendor {tag}", IsActive = false };
            db.AddRange(initiative, person, card, vendor);
            await db.SaveChangesAsync();
            (initiativeId, personId, rateCardId, vendorId) = (initiative.Id, person.Id, card.Id, vendor.Id);
        }

        var client = factory.CreateClient();
        var page = await client.GetStringAsync($"/Search?q={tag}");
        Assert.Contains("4 results", page);
        Assert.Contains($"/Initiatives/Details/{initiativeId}", page);
        Assert.Contains($"/Admin/People/Edit/{personId}", page);
        Assert.Contains($"/Admin/RateCards/Details/{rateCardId}", page);
        Assert.Contains($"/Admin/Vendors/Edit/{vendorId}", page);
        Assert.Contains("Inactive", page);

        page = await client.GetStringAsync($"/Search?q={tag.ToUpperInvariant()}");
        Assert.Contains("4 results", page);

        page = await client.GetStringAsync("/Search?q=no-such-thing-xyz");
        Assert.Contains("No results", page);
    }

    [Fact]
    public async Task Non_admin_only_sees_initiatives()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await using var f = new NoRoleFactory();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bu = await db.BusinessUnits.FirstAsync();
            var rt = await db.ResourceTypes.FirstAsync();
            var sen = await db.SeniorityLevels.FirstAsync();
            db.AddRange(
                new UserAccount { ObjectId = "no-role-user", Email = "no.role@example.com", DisplayName = "x", Role = AppRole.Viewer, Status = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow },
                new Initiative { Name = $"Zeta {tag}", BusinessUnit = bu, CreatedBy = "admin", TargetStart = new DateOnly(2027, 1, 1) },
                new Person { DisplayName = $"Pat {tag}", ResourceType = rt, Seniority = sen, BusinessUnit = bu, ResourcingClass = ResourcingClass.InternalFte, Location = "Onshore" },
                new Vendor { Name = $"Vendor {tag}" });
            await db.SaveChangesAsync();
        }

        var client = f.CreateClient(NoRedirect);
        var page = await client.GetStringAsync($"/Search?q={tag}");
        Assert.Contains("1 result for", page);
        Assert.Contains($"Zeta {tag}", page);
        Assert.DoesNotContain($"Pat {tag}", page);
        Assert.DoesNotContain($"Vendor {tag}", page);
    }

    [Fact]
    public async Task Pending_user_does_not_see_search_box()
    {
        await using var f = new NoRoleFactory();
        var client = f.CreateClient(NoRedirect);
        var page = await client.GetStringAsync("/Access");
        Assert.DoesNotContain("id=\"global-search\"", page);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Search?q=zz")).StatusCode);
    }

    [Fact]
    public async Task Layout_has_global_search_box_and_skip_link()
    {
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/Initiatives");
        Assert.Contains("id=\"global-search\"", page);
        Assert.Contains("Skip to content", page);
        Assert.Contains("data-remember-filters", page);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Search?q=zz")).StatusCode);
    }
}
