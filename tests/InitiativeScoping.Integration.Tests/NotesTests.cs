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

public class NotesTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);

    [Fact]
    public async Task Users_add_notes_authors_and_admins_delete_viewers_read_only()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
        await using var userFactory = new SharedDbFactory { DbPath = dbPath, UserId = "user-a", Role = "User" };
        await using var otherFactory = new SharedDbFactory { DbPath = dbPath, UserId = "user-b", Role = "User" };
        await using var adminFactory = new SharedDbFactory { DbPath = dbPath, UserId = "admin-n", Role = "Admin" };
        await using var viewerFactory = new SharedDbFactory { DbPath = dbPath, UserId = "viewer-n", Role = "Viewer" };
        var user = userFactory.CreateClient(NoRedirect);
        var other = otherFactory.CreateClient(NoRedirect);
        var admin = adminFactory.CreateClient(NoRedirect);
        var viewer = viewerFactory.CreateClient(NoRedirect);

        var id = await CreateInitiativeAsync(user, userFactory, "Noted");
        var details = $"/Initiatives/Details/{id}";
        Assert.Contains("No notes yet", await user.GetStringAsync(details));

        // Empty and oversized bodies are rejected.
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(user, details, $"/Initiatives/{id}/Notes", new() { ["body"] = "   " })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(user, details, $"/Initiatives/{id}/Notes", new() { ["body"] = new string('x', 4001) })).StatusCode);
        Assert.Empty(await NotesAsync(userFactory, id));

        // Viewer cannot add.
        var viewerAdd = await PostFormAsync(viewer, details, $"/Initiatives/{id}/Notes", new() { ["body"] = "sneaky" });
        Assert.Equal(HttpStatusCode.Forbidden, viewerAdd.StatusCode);
        Assert.DoesNotContain("Add a note", await viewer.GetStringAsync(details));

        var add = await PostFormAsync(user, details, $"/Initiatives/{id}/Notes", new() { ["body"] = "  Sponsor wants Q3 start.  " });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.EndsWith("#pane-notes", add.Headers.Location!.ToString());
        var note = Assert.Single(await NotesAsync(userFactory, id));
        Assert.Equal("Sponsor wants Q3 start.", note.Body);
        Assert.Equal("user-a", note.CreatedBy);
        Assert.Null(note.ForecastBaselineId);

        var html = await other.GetStringAsync(details);
        Assert.Contains("Sponsor wants Q3 start.", html);
        Assert.Contains("User user-a", html);
        Assert.DoesNotContain($"/Notes/{note.Id}/Delete", html); // not the author
        Assert.Contains($"/Notes/{note.Id}/Delete", await user.GetStringAsync(details));
        Assert.Contains($"/Notes/{note.Id}/Delete", await admin.GetStringAsync(details));
        Assert.DoesNotContain($"/Notes/{note.Id}/Delete", await viewer.GetStringAsync(details));

        // Other user and viewer cannot delete; unknown note is 404; author can.
        Assert.Equal(HttpStatusCode.Forbidden, (await PostFormAsync(other, details, $"/Initiatives/{id}/Notes/{note.Id}/Delete", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostFormAsync(viewer, details, $"/Initiatives/{id}/Notes/{note.Id}/Delete", new())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(user, details, $"/Initiatives/{id}/Notes/{note.Id + 999}/Delete", new())).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(user, details, $"/Initiatives/{id}/Notes/{note.Id}/Delete", new())).StatusCode);
        Assert.Empty(await NotesAsync(userFactory, id));

        // Admin deletes someone else's note.
        await PostFormAsync(other, details, $"/Initiatives/{id}/Notes", new() { ["body"] = "From B" });
        var bNote = Assert.Single(await NotesAsync(userFactory, id));
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(admin, details, $"/Initiatives/{id}/Notes/{bNote.Id}/Delete", new())).StatusCode);
        Assert.Empty(await NotesAsync(userFactory, id));

        using var scope = adminFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actions = await db.AuditEvents.Where(a => a.Entity == nameof(Initiative) && a.EntityId == id.ToString()).Select(a => a.Action).ToListAsync();
        Assert.Equal(2, actions.Count(a => a == AuditActions.NoteAdded));
        Assert.Equal(2, actions.Count(a => a == AuditActions.NoteDeleted));
    }

    [Fact]
    public async Task Baseline_notes_are_scoped_to_the_baseline_and_shown_on_details()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
        await using var adminFactory = new SharedDbFactory { DbPath = dbPath, UserId = "admin-b", Role = "Admin" };
        var admin = adminFactory.CreateClient(NoRedirect);

        var id = await CreateInitiativeAsync(admin, adminFactory, "Baselined");
        var otherId = await CreateInitiativeAsync(admin, adminFactory, "Other");
        var details = $"/Initiatives/Details/{id}";
        await PostFormAsync(admin, details, $"/Initiatives/AddPhase/{id}", new() { ["Name"] = "Build", ["PlannedStart"] = "2026-03-01", ["PlannedEnd"] = "2026-04-30" });
        int phaseId, typeId;
        using (var scope = adminFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            phaseId = (await db.Phases.FirstAsync(p => p.InitiativeId == id)).Id;
            typeId = (await db.ResourceTypes.FirstAsync(t => t.Name == "Software Engineer")).Id;
        }

        await PostFormAsync(admin, details, $"/Initiatives/AddAllocation/{id}", new()
        {
            ["PhaseId"] = phaseId.ToString(), ["ResourceTypeId"] = typeId.ToString(), ["SeniorityId"] = "3",
            ["Location"] = "Onshore", ["ResourcingClass"] = nameof(ResourcingClass.InternalFte), ["Quantity"] = "1", ["EstimatedHours"] = "50"
        });
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(admin, details, $"/Initiatives/{id}/Activate", new())).StatusCode);
        int baselineId;
        using (var scope = adminFactory.Services.CreateScope())
        {
            baselineId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().ForecastBaselines.SingleAsync(b => b.InitiativeId == id)).Id;
        }

        var baselines = $"/Initiatives/{id}/Baselines";
        var page = await admin.GetStringAsync(baselines);
        Assert.Contains("Notes on baseline v1", page);
        Assert.Contains("No notes on this baseline yet", page);

        // Baseline of another initiative is refused.
        Assert.Equal(HttpStatusCode.NotFound, (await PostFormAsync(admin, details, $"/Initiatives/{otherId}/Notes", new() { ["body"] = "x", ["baselineId"] = baselineId.ToString() })).StatusCode);

        var add = await PostFormAsync(admin, baselines, $"/Initiatives/{id}/Notes", new()
        {
            ["body"] = "Signed off by finance", ["baselineId"] = baselineId.ToString(), ["returnUrl"] = $"{baselines}?version=1#notes-baseline-{baselineId}"
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.Equal($"{baselines}?version=1#notes-baseline-{baselineId}", add.Headers.Location!.ToString());
        var note = Assert.Single(await NotesAsync(adminFactory, id));
        Assert.Equal(baselineId, note.ForecastBaselineId);

        page = await admin.GetStringAsync(baselines);
        Assert.Contains("Signed off by finance", page);
        var html = await admin.GetStringAsync(details);
        Assert.Contains("Signed off by finance", html);
        Assert.Contains("baseline v1", html);

        // Off-site returnUrl falls back to Details.
        var external = await PostFormAsync(admin, details, $"/Initiatives/{id}/Notes", new() { ["body"] = "plain", ["returnUrl"] = "https://evil.example/" });
        Assert.Equal(HttpStatusCode.Redirect, external.StatusCode);
        Assert.StartsWith(details, external.Headers.Location!.ToString());
    }

    private static async Task<List<InitiativeNote>> NotesAsync(WebApplicationFactory<Program> f, int id)
    {
        using var scope = f.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().InitiativeNotes.AsNoTracking().Where(n => n.InitiativeId == id).ToListAsync();
    }

    private static async Task<int> CreateInitiativeAsync(HttpClient client, WebApplicationFactory<Program> f, string name)
    {
        int buId;
        using (var scope = f.Services.CreateScope())
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
