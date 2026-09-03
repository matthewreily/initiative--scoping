using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class HardeningTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Theory]
    [InlineData("/")]
    [InlineData("/Portfolio")]
    [InlineData("/health")]
    [InlineData("/Portfolio/Export?format=csv")]
    public async Task Responses_carry_security_headers(string url)
    {
        var client = factory.CreateClient(NoRedirect);
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public async Task Not_found_and_forbidden_render_friendly_pages_with_original_status()
    {
        var client = factory.CreateClient(NoRedirect);
        var missing = await client.GetAsync("/Initiatives/Details/999999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var html = await missing.Content.ReadAsStringAsync();
        Assert.Contains("404", html);
        Assert.Contains("Not found", html);
        Assert.Contains("Return to home", html);

        var unknownRoute = await client.GetAsync("/no/such/page");
        Assert.Equal(HttpStatusCode.NotFound, unknownRoute.StatusCode);
        Assert.Contains("Not found", await unknownRoute.Content.ReadAsStringAsync());

        await using var viewerFactory = new ViewerOnlyFactory();
        var viewer = viewerFactory.CreateClient(NoRedirect);
        var forbidden = await viewer.GetAsync("/Admin/BusinessUnits");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("Access denied", await forbidden.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Oversized_actuals_upload_is_rejected_before_processing()
    {
        var client = factory.CreateClient(NoRedirect);
        var token = TokenRegex.Match(await client.GetStringAsync("/Actuals")).Groups[1].Value;
        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(new byte[11 * 1024 * 1024]);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "File", "huge.csv");

        int importsBefore;
        using (var scope = factory.Services.CreateScope())
        {
            importsBefore = await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActualsImports.CountAsync();
        }

        // TestServer has no transport body limit (Kestrel enforces [RequestSizeLimit] in production), so this exercises the controller guard.
        var response = await client.PostAsync("/Actuals/Import", content);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("exceeds the 10 MB import limit", await client.GetStringAsync("/Actuals"));

        using (var scope = factory.Services.CreateScope())
        {
            Assert.Equal(importsBefore, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ActualsImports.CountAsync());
        }
    }

    [Fact]
    public async Task Portfolio_and_exports_stay_correct_and_fast_with_many_initiatives()
    {
        const int count = 60;
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (buId, typeId) = await SeedInitiativesAsync(tag, count);

        var client = factory.CreateClient(NoRedirect);
        var stopwatch = Stopwatch.StartNew();
        var html = await client.GetStringAsync($"/Portfolio?businessUnitId={buId}");
        stopwatch.Stop();

        Assert.Equal(count, Regex.Matches(html, $"Scale {tag} \\d{{3}}").Select(m => m.Value).Distinct().Count());
        Assert.Contains("Over threshold", html);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Portfolio took {stopwatch.Elapsed}");

        var csv = await client.GetStringAsync("/Portfolio/Export?format=csv");
        Assert.Equal(count, Regex.Matches(csv, $",Scale {tag} \\d{{3}},").Count);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.ResourceTypes.AnyAsync(t => t.Id == typeId));
    }

    private async Task<(int BuId, int TypeId)> SeedInitiativesAsync(string tag, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bu = new BusinessUnit { Name = $"Scale BU {tag}" };
        db.BusinessUnits.Add(bu);
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        await db.SaveChangesAsync();

        var initiatives = new List<Initiative>();
        for (var n = 0; n < count; n++)
        {
            var initiative = new Initiative
            {
                Name = $"Scale {tag} {n:D3}",
                BusinessUnitId = bu.Id,
                Status = InitiativeStatus.Active,
                TargetStart = new DateOnly(2026, 3, 1),
                CreatedBy = "seed"
            };
            var phase = new Phase { Name = "Build", Sequence = 1, PlannedStart = new DateOnly(2026, 3, 1), PlannedEnd = new DateOnly(2026, 4, 30) };
            initiative.Phases.Add(phase);
            for (var a = 0; a < 5; a++)
            {
                initiative.Allocations.Add(new InitiativeAllocation
                {
                    Phase = phase, ResourceTypeId = typeId, Seniority = Seniority.Senior, Location = "Onshore",
                    ResourcingClass = ResourcingClass.InternalFte, Quantity = 1, EstimatedHours = 100
                });
            }

            db.Initiatives.Add(initiative);
            db.ActualAdjustments.Add(new ActualAdjustment { Initiative = initiative, Hours = 0, Cost = n % 2 == 0 ? 70000 : 100, Reason = "seed", CreatedBy = "seed", CreatedAt = DateTimeOffset.UtcNow });
            initiatives.Add(initiative);
        }

        await db.SaveChangesAsync();

        foreach (var initiative in initiatives)
        {
            var baseline = new ForecastBaseline
            {
                InitiativeId = initiative.Id, Version = 1, IsCurrent = true, SnapshotBy = "seed", SnapshotAt = DateTimeOffset.UtcNow, TotalHours = 500, TotalCost = 60000
            };
            foreach (var allocation in initiative.Allocations)
            {
                baseline.Lines.Add(new ForecastBaselineLine
                {
                    PhaseId = allocation.PhaseId, ResourceTypeId = typeId, Seniority = Seniority.Senior, Location = "Onshore",
                    ResourcingClass = ResourcingClass.InternalFte, Hours = 100, HourlyRate = 120, Cost = 12000
                });
            }

            db.ForecastBaselines.Add(baseline);
        }

        await db.SaveChangesAsync();
        return (bu.Id, typeId);
    }
}
