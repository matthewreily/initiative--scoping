using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

/// <summary>
/// Fixed-duration planning: Jan 5 – Mar 27 2026 is 12 weeks = 84 calendar days = 60 working days (no holidays).
/// </summary>
public class FixedDurationTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Fixed_duration_requires_target_end_on_or_after_start()
    {
        var client = factory.CreateClient(NoRedirect);
        var buId = (await SeededBusinessUnitIdAsync()).ToString();

        var missing = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "No end", ["BusinessUnitId"] = buId, ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.FixedDuration), ["TargetStart"] = "2026-01-05"
        });
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("Target end is required", await missing.Content.ReadAsStringAsync());

        var inverted = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "Inverted", ["BusinessUnitId"] = buId, ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.FixedDuration), ["TargetStart"] = "2026-01-05", ["TargetEnd"] = "2026-01-04"
        });
        Assert.Equal(HttpStatusCode.OK, inverted.StatusCode);
        Assert.Contains("on or after target start", await inverted.Content.ReadAsStringAsync());

        // Effort-driven ignores TargetEnd entirely.
        var effort = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "Effort", ["BusinessUnitId"] = buId, ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.EffortDriven), ["TargetStart"] = "2026-01-05", ["TargetEnd"] = "2025-01-01"
        });
        Assert.Equal(HttpStatusCode.Redirect, effort.StatusCode);
        var effortId = int.Parse(DetailsRegex.Match(effort.Headers.Location!.ToString()).Groups[1].Value);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Initiatives.SingleAsync(i => i.Id == effortId);
        Assert.Equal(PlanningMode.EffortDriven, saved.PlanningMode);
        Assert.Null(saved.TargetEnd);
    }

    [Fact]
    public async Task Phases_must_tile_the_fixed_window_and_hours_are_computed_from_percent()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateFixedAsync(client, "Tiling test");
        var details = $"/Initiatives/Details/{id}";

        var html = await client.GetStringAsync(details);
        Assert.Contains("Fixed duration", html);
        Assert.Contains("60 working days", html);

        var wrongStart = await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Discovery", ["PlannedStart"] = "2026-01-06", ["PlannedEnd"] = "2026-01-30" });
        Assert.Contains("First phase must start on 2026-01-05", await client.GetStringAsync(details));
        Assert.Equal(HttpStatusCode.Redirect, wrongStart.StatusCode);

        var tooLong = await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Everything", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-03-28" });
        Assert.Equal(HttpStatusCode.Redirect, tooLong.StatusCode);
        Assert.Contains("ends after the initiative&#x27;s target end 2026-03-27", await client.GetStringAsync(details));

        // Partial coverage is allowed while drafting but blocks activation until the window is fully tiled.
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Everything", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-01-30" });
        Assert.Contains("Last phase must end on 2026-03-27", await client.GetStringAsync(details));
        var (phaseId, typeId) = await FirstPhaseAndTypeAsync(id, "Software Engineer");
        var extend = await PostFormAsync(client, $"/Initiatives/EditPhase/{phaseId}", $"/Initiatives/EditPhase/{phaseId}", new()
        {
            ["Name"] = "Everything", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-03-27", ["Reason"] = "Cover window"
        });
        Assert.Equal(HttpStatusCode.Redirect, extend.StatusCode);
        html = await client.GetStringAsync(details);
        Assert.DoesNotContain("Last phase must end on", html);
        Assert.Contains("id=\"allocationpercent\"", html);
        Assert.DoesNotContain("id=\"estimatedhours\"", html);

        var noPercent = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["EstimatedHours"] = "100"
        });
        Assert.Equal(HttpStatusCode.Redirect, noPercent.StatusCode);
        Assert.Contains("Allocation % is required", await client.GetStringAsync(details));

        var half = await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Senior),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "2", ["AllocationPercent"] = "50", ["EstimatedHours"] = "999"
        });
        Assert.Equal(HttpStatusCode.Redirect, half.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var allocation = await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id);
            Assert.Equal(50m, allocation.AllocationPercent);
            Assert.Equal(240m, allocation.EstimatedHours); // 50% × 60 days × 8h, manual 999 ignored
        }

        html = await client.GetStringAsync(details);
        Assert.Contains("480.0", html); // 2 people × 240h
        Assert.Contains("50%", html);
        Assert.Contains("average staffing 1 FTE", html);

        // The initiative window cannot shrink past its phases; shrink the phase first (hours recompute), then the window.
        var badWindow = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", await EditFieldsAsync(id, "2026-01-05", "2026-02-27"));
        Assert.Equal(HttpStatusCode.OK, badWindow.StatusCode);
        Assert.Contains("ends after the initiative&#x27;s target end 2026-02-27", await badWindow.Content.ReadAsStringAsync());

        var shrink = await PostFormAsync(client, $"/Initiatives/EditPhase/{phaseId}", $"/Initiatives/EditPhase/{phaseId}", new()
        {
            ["Name"] = "Everything", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-02-27", ["Reason"] = "Shorter"
        });
        Assert.Equal(HttpStatusCode.Redirect, shrink.StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(160m, (await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id)).EstimatedHours); // 50% × 40 × 8
        }

        // Moving the window start shifts capacity: Jan 12 – Feb 27 is 35 working days, but the phase must start on the new start.
        var badStart = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", await EditFieldsAsync(id, "2026-01-12", "2026-02-27"));
        Assert.Equal(HttpStatusCode.OK, badStart.StatusCode);
        Assert.Contains("First phase must start on 2026-01-12", await badStart.Content.ReadAsStringAsync());

        var windowOk = await PostFormAsync(client, $"/Initiatives/Edit/{id}", $"/Initiatives/Edit/{id}", await EditFieldsAsync(id, "2026-01-05", "2026-02-27"));
        Assert.Equal(HttpStatusCode.Redirect, windowOk.StatusCode);
        html = await client.GetStringAsync(details);
        Assert.Contains("Initiative updated", html);
        Assert.Contains("40 working days", html);
    }

    [Fact]
    public async Task Phase_date_change_recomputes_hours_and_records_history()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateFixedAsync(client, "Recompute test");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Discovery", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-01-30" });
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-01-31", ["PlannedEnd"] = "2026-03-27" });
        var (discoveryId, typeId) = await FirstPhaseAndTypeAsync(id, "QA Analyst");

        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = discoveryId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Mid),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["AllocationPercent"] = "100"
        });

        int buildId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(160m, (await db.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id)).EstimatedHours); // 20 working days
            buildId = (await db.Phases.SingleAsync(p => p.InitiativeId == id && p.Name == "Build")).Id;
        }

        // Gap: Build would start two days after Discovery ends.
        var gap = await PostFormAsync(client, $"/Initiatives/EditPhase/{discoveryId}", $"/Initiatives/EditPhase/{discoveryId}", new()
        {
            ["Name"] = "Discovery", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-01-28", ["Reason"] = "gap"
        });
        Assert.Equal(HttpStatusCode.OK, gap.StatusCode);
        Assert.Contains("must start on 2026-01-29", await gap.Content.ReadAsStringAsync());

        // Move the boundary: extend Build backwards first is also a gap/overlap, so shrink Discovery + extend Build atomically isn't possible;
        // extend Discovery into Build (overlap) is rejected too.
        var overlap = await PostFormAsync(client, $"/Initiatives/EditPhase/{discoveryId}", $"/Initiatives/EditPhase/{discoveryId}", new()
        {
            ["Name"] = "Discovery", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-02-06", ["Reason"] = "overlap"
        });
        Assert.Equal(HttpStatusCode.OK, overlap.StatusCode);
        Assert.Contains("must start on 2026-02-07", await overlap.Content.ReadAsStringAsync());

        // Adjust the build start first so the boundary moves consistently.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var build = await db.Phases.SingleAsync(p => p.Id == buildId);
            build.PlannedStart = new DateOnly(2026, 2, 7);
            await db.SaveChangesAsync();
        }

        var ok = await PostFormAsync(client, $"/Initiatives/EditPhase/{discoveryId}", $"/Initiatives/EditPhase/{discoveryId}", new()
        {
            ["Name"] = "Discovery", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-02-06", ["Reason"] = "Longer discovery"
        });
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);

        using var verify = factory.Services.CreateScope();
        var verifyDb = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var allocation = await verifyDb.InitiativeAllocations.SingleAsync(a => a.InitiativeId == id);
        Assert.Equal(200m, allocation.EstimatedHours); // 25 working days × 8h
        var phase = await verifyDb.Phases.Include(p => p.DateHistory).SingleAsync(p => p.Id == discoveryId);
        var history = Assert.Single(phase.DateHistory);
        Assert.Equal("Longer discovery", history.Reason);
    }

    [Fact]
    public async Task Apply_size_tiles_window_and_derives_percentages_that_reproduce_template_hours()
    {
        var client = factory.CreateClient(NoRedirect);
        var id = await CreateFixedAsync(client, "Fixed sizing test");
        var details = $"/Initiatives/Details/{id}";

        var response = await PostFormAsync(client, details, $"/Initiatives/ApplySize/{id}", new()
        {
            ["Method"] = nameof(SizingMethod.TShirt), ["SizeKey"] = "L", ["Location"] = "Onshore",
            ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Replace"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initiative = await db.Initiatives.Include(i => i.Phases).Include(i => i.Allocations).SingleAsync(i => i.Id == id);
        var phases = initiative.Phases.OrderBy(p => p.Sequence).ToList();
        Assert.Equal(["Discovery", "Build", "Launch"], phases.Select(p => p.Name));
        Assert.Equal(new DateOnly(2026, 1, 5), phases[0].PlannedStart);
        Assert.Equal(new DateOnly(2026, 3, 27), phases[^1].PlannedEnd);
        for (var i = 1; i < phases.Count; i++)
        {
            Assert.Equal(phases[i - 1].PlannedEnd.AddDays(1), phases[i].PlannedStart);
        }

        Assert.Equal(6, initiative.Allocations.Count);
        Assert.All(initiative.Allocations, a => Assert.True(a.AllocationPercent is > 0 and <= 100));
        // Percentages are rounded to 2 dp, so the total reconciles to the 480h conversion within rounding noise.
        var total = initiative.Allocations.Sum(a => a.Quantity * a.EstimatedHours);
        Assert.InRange(total, 479m, 481m);
    }

    [Fact]
    public async Task Effort_driven_apply_size_is_unchanged()
    {
        var client = factory.CreateClient(NoRedirect);
        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "Effort sizing", ["BusinessUnitId"] = (await SeededBusinessUnitIdAsync()).ToString(),
            ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-01-05"
        });
        var id = int.Parse(DetailsRegex.Match(response.Headers.Location!.ToString()).Groups[1].Value);
        await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/ApplySize/{id}", new()
        {
            ["Method"] = nameof(SizingMethod.TShirt), ["SizeKey"] = "L", ["Location"] = "Onshore",
            ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Replace"] = "true"
        });

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var initiative = await db.Initiatives.Include(i => i.Phases).Include(i => i.Allocations).SingleAsync(i => i.Id == id);
        Assert.Equal(480m, initiative.Allocations.Sum(a => a.Quantity * a.EstimatedHours));
        Assert.All(initiative.Allocations, a => Assert.Null(a.AllocationPercent));
        Assert.Equal(new DateOnly(2026, 1, 5), initiative.Phases.OrderBy(p => p.Sequence).First().PlannedStart);
    }

    [Fact]
    public async Task Work_calendar_hours_per_day_and_holidays_change_computed_hours()
    {
        await using var isolated = new WebAppFactory();
        var client = isolated.CreateClient(NoRedirect);

        var index = await client.GetStringAsync("/Admin/WorkCalendar");
        Assert.Contains("value=\"8\"", index);

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/WorkCalendar", "/Admin/WorkCalendar/Settings", new() { ["HoursPerDay"] = "6" })).StatusCode);

        var weekend = await PostFormAsync(client, "/Admin/WorkCalendar/CreateHoliday", "/Admin/WorkCalendar/CreateHoliday", new() { ["Date"] = "2026-01-10", ["Name"] = "Saturday" });
        Assert.Equal(HttpStatusCode.OK, weekend.StatusCode);
        Assert.Contains("choose a weekday", await weekend.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/WorkCalendar/CreateHoliday", "/Admin/WorkCalendar/CreateHoliday", new() { ["Date"] = "2026-01-19", ["Name"] = "MLK Day" })).StatusCode);
        var duplicate = await PostFormAsync(client, "/Admin/WorkCalendar/CreateHoliday", "/Admin/WorkCalendar/CreateHoliday", new() { ["Date"] = "2026-01-19", ["Name"] = "Again" });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("already exists", await duplicate.Content.ReadAsStringAsync());

        int buId;
        using (var scope = isolated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(6m, (await db.WorkCalendarSettings.SingleAsync()).HoursPerDay);
            Assert.Single(await db.Holidays.ToListAsync());
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(Holiday) && a.Action == "Create"));
            Assert.True(await db.AuditEvents.AnyAsync(a => a.Entity == nameof(WorkCalendarSettings) && a.Action == "Update"));
            buId = (await db.BusinessUnits.FirstAsync()).Id;
        }

        var create = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "Calendar", ["BusinessUnitId"] = buId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.FixedDuration), ["TargetStart"] = "2026-01-05", ["TargetEnd"] = "2026-01-30"
        });
        var id = int.Parse(DetailsRegex.Match(create.Headers.Location!.ToString()).Groups[1].Value);
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(client, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "All", ["PlannedStart"] = "2026-01-05", ["PlannedEnd"] = "2026-01-30" });

        int phaseId, typeId;
        using (var scope = isolated.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync()).Id;
        }

        await PostFormAsync(client, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["Seniority"] = nameof(Seniority.Mid),
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["AllocationPercent"] = "100"
        });

        using var verify = isolated.Services.CreateScope();
        var allocation = await verify.ServiceProvider.GetRequiredService<AppDbContext>().InitiativeAllocations.SingleAsync(a => a.InitiativeId == id);
        Assert.Equal(114m, allocation.EstimatedHours); // 20 weekdays − 1 holiday = 19 × 6h
        Assert.Contains("19 working days", await client.GetStringAsync(details));

        var deleteHoliday = await PostFormAsync(client, "/Admin/WorkCalendar", $"/Admin/WorkCalendar/DeleteHoliday/{(await HolidayIdAsync(isolated))}", new());
        Assert.Equal(HttpStatusCode.Redirect, deleteHoliday.StatusCode);
    }

    private static async Task<int> HolidayIdAsync(WebAppFactory f)
    {
        using var scope = f.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Holidays.SingleAsync()).Id;
    }

    private async Task<int> CreateFixedAsync(HttpClient client, string name)
    {
        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = name, ["BusinessUnitId"] = (await SeededBusinessUnitIdAsync()).ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.FixedDuration), ["TargetStart"] = "2026-01-05", ["TargetEnd"] = "2026-03-27"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var match = DetailsRegex.Match(response.Headers.Location!.ToString());
        Assert.True(match.Success, $"Unexpected redirect {response.Headers.Location}");
        return int.Parse(match.Groups[1].Value);
    }

    private async Task<Dictionary<string, string>> EditFieldsAsync(int id, string start, string end)
    {
        using var scope = factory.Services.CreateScope();
        var i = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Initiatives.SingleAsync(x => x.Id == id);
        return new()
        {
            ["Id"] = id.ToString(), ["Name"] = i.Name, ["BusinessUnitId"] = i.BusinessUnitId.ToString(), ["SizingMethod"] = nameof(SizingMethod.Direct),
            ["PlanningMode"] = nameof(PlanningMode.FixedDuration), ["TargetStart"] = start, ["TargetEnd"] = end
        };
    }

    private async Task<int> SeededBusinessUnitIdAsync()
    {
        using var scope = factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id;
    }

    private async Task<(int PhaseId, int ResourceTypeId)> FirstPhaseAndTypeAsync(int initiativeId, string typeName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phaseId = (await db.Phases.OrderBy(p => p.Sequence).FirstAsync(p => p.InitiativeId == initiativeId)).Id;
        var typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == typeName)).Id;
        return (phaseId, typeId);
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
