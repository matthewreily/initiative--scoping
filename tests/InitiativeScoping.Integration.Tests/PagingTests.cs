using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class PagingTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };

    [Fact]
    public void Window_elides_far_pages_but_keeps_ends_and_neighbours()
    {
        Assert.Equal(new int?[] { 1, 2, 3 }, Paging.Window(2, 3));
        Assert.Equal(new int?[] { 1, null, 8, 9, 10, 11, 12, null, 40 }, Paging.Window(10, 40));
        Assert.Equal(new int?[] { 1, 2, 3, null, 40 }, Paging.Window(1, 40));
        Assert.Equal(25, Paging.NormalizeSize(25, 50));
        Assert.Equal(50, Paging.NormalizeSize(7, 50));
        Assert.Equal(1, Paging.ClampPage(0, 10, 25));
        Assert.Equal(2, Paging.ClampPage(99, 30, 25));
    }

    [Fact]
    public async Task Portfolio_sorts_the_whole_filtered_set_and_keeps_filters_in_page_links()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        int buId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var bu = new BusinessUnit { Name = $"Paging BU {tag}" };
            db.BusinessUnits.Add(bu);
            foreach (var (name, cost) in new[] { ("Alpha", 300m), ("Bravo", 100m), ("Charlie", 200m) })
            {
                var initiative = new Initiative { Name = $"{name} {tag}", BusinessUnit = bu, Status = InitiativeStatus.Active, TargetStart = new DateOnly(2026, 3, 1), CreatedBy = "seed" };
                db.Initiatives.Add(initiative);
                db.ActualAdjustments.Add(new ActualAdjustment { Initiative = initiative, Hours = 0, Cost = cost, Reason = "seed", CreatedBy = "seed", CreatedAt = DateTimeOffset.UtcNow });
            }

            await db.SaveChangesAsync();
            buId = bu.Id;
        }

        var client = factory.CreateClient(NoRedirect);
        var byName = WebUtility.HtmlDecode(await client.GetStringAsync($"/Portfolio?businessUnitId={buId}"));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, Names(byName, tag));

        var byActualDesc = WebUtility.HtmlDecode(await client.GetStringAsync($"/Portfolio?businessUnitId={buId}&sort=actual&dir=desc"));
        Assert.Equal(new[] { "Alpha", "Charlie", "Bravo" }, Names(byActualDesc, tag));
        Assert.Contains("aria-sort=\"descending\"", byActualDesc);
        Assert.Contains($"businessUnitId={buId}&sort=actual&dir=desc", byActualDesc.Replace("&amp;", "&"));

        // Rows without a baseline sink to the bottom regardless of direction; unknown sort keys fall back to the default order.
        var byBaseline = WebUtility.HtmlDecode(await client.GetStringAsync($"/Portfolio?businessUnitId={buId}&sort=baseline&dir=desc"));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, Names(byBaseline, tag));
        var bogus = WebUtility.HtmlDecode(await client.GetStringAsync($"/Portfolio?businessUnitId={buId}&sort=nope&size=7&page=99"));
        Assert.Equal(new[] { "Alpha", "Bravo", "Charlie" }, Names(bogus, tag));
        Assert.Contains("Showing 1–3 of 3", bogus);
        Assert.Contains("Total (3)", bogus);
    }

    [Fact]
    public async Task Audit_pages_server_side_and_keeps_filters_and_size_in_links()
    {
        var entity = $"PagingEntity{Guid.NewGuid():N}";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var n = 0; n < 60; n++)
            {
                db.AuditEvents.Add(new AuditEvent { Entity = entity, EntityId = n.ToString(), Action = "Seed", UserId = "seed", At = DateTimeOffset.UtcNow });
            }

            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient(NoRedirect);
        var page1 = await client.GetStringAsync($"/Audit?entity={entity}");
        Assert.Equal(50, Regex.Matches(page1, $"<td>{entity}</td>").Count);
        Assert.Contains("Showing 1&ndash;50 of 60", page1);
        Assert.Contains($"entity={entity}&amp;page=2", page1);

        var page2 = await client.GetStringAsync($"/Audit?entity={entity}&page=2");
        Assert.Equal(10, Regex.Matches(page2, $"<td>{entity}</td>").Count);
        Assert.Contains("Showing 51&ndash;60 of 60", page2);

        var small = await client.GetStringAsync($"/Audit?entity={entity}&size=25&page=3");
        Assert.Equal(10, Regex.Matches(small, $"<td>{entity}</td>").Count);
        Assert.Contains($"entity={entity}&amp;page=2&amp;size=25", small);

        var beyond = await client.GetStringAsync($"/Audit?entity={entity}&page=42");
        Assert.Contains("Showing 51&ndash;60 of 60", beyond);
    }

    private static string[] Names(string html, string tag) =>
        Regex.Matches(html, $"<td><a [^>]*>(\\w+) {tag}</a></td>").Select(m => m.Groups[1].Value).ToArray();
}
