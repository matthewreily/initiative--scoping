using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

/// <summary>Dev user signs in with no roles at all; access comes only from the UserAccounts table (or bootstrap config).</summary>
public class NoRoleFactory : WebAppFactory
{
    public string DbPath { get; } = Path.Combine(Path.GetTempPath(), $"is-test-{Guid.NewGuid():N}.db");
    public string UserId { get; init; } = "no-role-user";
    public string? Email { get; init; } = "no.role@example.com";
    public string[] BootstrapAdmins { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbPath}");
        builder.UseSetting("Auth:Dev:UserId", UserId);
        builder.UseSetting("Auth:Dev:DisplayName", "No Role");
        builder.UseSetting("Auth:Dev:Email", Email ?? "");
        for (var i = 0; i < 5; i++)
        {
            builder.UseSetting($"Auth:Dev:Roles:{i}", "");
        }

        for (var i = 0; i < BootstrapAdmins.Length; i++)
        {
            builder.UseSetting($"Auth:BootstrapAdmins:{i}", BootstrapAdmins[i]);
        }
    }
}

public class UserAccessTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Unknown_user_is_gated_to_access_page_and_can_request_access()
    {
        await using var f = new NoRoleFactory();
        var client = f.CreateClient(NoRedirect);

        var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/Access", home.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Initiatives")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        var page = await client.GetStringAsync("/Access");
        Assert.Contains("Request access", page);
        Assert.Contains("no.role@example.com", page);

        var post = await PostFormAsync(client, "/Access", "/Access/Request", new() { ["Note"] = "Need to plan Q3" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        page = await client.GetStringAsync("/Access");
        Assert.Contains("waiting for an administrator", page);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(await db.UserAccounts.ToListAsync());
        Assert.Equal("no-role-user", row.ObjectId);
        Assert.Equal("no.role@example.com", row.Email);
        Assert.Equal(UserAccountStatus.Pending, row.Status);
        Assert.Equal("Need to plan Q3", row.Note);

        // Pending still has no access.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Portfolio")).StatusCode);
    }

    [Fact]
    public async Task Active_row_grants_role_disabled_row_removes_it()
    {
        await using var f = new NoRoleFactory();
        await Seed(f, new UserAccount { ObjectId = "no-role-user", Email = "no.role@example.com", DisplayName = "x", Role = AppRole.User, Status = UserAccountStatus.Active });
        var client = f.CreateClient(NoRedirect);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Initiatives/Create")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Access")).StatusCode);

        // Profile upserted from the sign-in claims.
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserAccounts.SingleAsync();
            Assert.Equal("No Role", row.DisplayName);
            Assert.NotNull(row.LastSeenAt);
            row.Status = UserAccountStatus.Disabled;
            await db.SaveChangesAsync();
        }

        f.Services.GetRequiredService<InitiativeScoping.Web.Authorization.AccessCache>().Invalidate();
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Initiatives")).StatusCode);
        Assert.Contains("disabled", await client.GetStringAsync("/Access"));
    }

    [Fact]
    public async Task Row_added_by_email_is_linked_to_object_id_at_first_sign_in()
    {
        await using var f = new NoRoleFactory();
        await Seed(f, new UserAccount { ObjectId = null, Email = "NO.ROLE@example.com", DisplayName = "Pre-added", Role = AppRole.User, Status = UserAccountStatus.Active });
        int initiativeId;
        using (var seed = f.Services.CreateScope())
        {
            // Admin made the pre-added user an Owner via the member picker, which stores the e-mail key until sign-in.
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var bu = await db.BusinessUnits.FirstAsync();
            var initiative = new Initiative { Name = "Pre-linked", BusinessUnit = bu, CreatedBy = "admin", TargetStart = new DateOnly(2027, 1, 1) };
            initiative.Members.Add(new InitiativeMember { UserId = "NO.ROLE@example.com", Role = InitiativeMemberRole.Owner });
            db.Initiatives.Add(initiative);
            await db.SaveChangesAsync();
            initiativeId = initiative.Id;
        }

        var client = f.CreateClient(NoRedirect);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Portfolio")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Admin/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Initiatives/Edit/{initiativeId}")).StatusCode);

        using var scope = f.Services.CreateScope();
        var verify = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await verify.UserAccounts.SingleAsync();
        Assert.Equal("no-role-user", row.ObjectId);
        var member = await verify.InitiativeMembers.SingleAsync(m => m.InitiativeId == initiativeId);
        Assert.Equal("no-role-user", member.UserId);
        Assert.Equal(InitiativeMemberRole.Owner, member.Role);
    }

    [Fact]
    public async Task Bootstrap_admin_gets_admin_without_a_row_and_row_is_created()
    {
        await using var f = new NoRoleFactory { BootstrapAdmins = ["NO.role@example.com"] };
        var client = f.CreateClient(NoRedirect);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Users")).StatusCode);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.UserAccounts.SingleAsync();
        Assert.Equal(AppRole.Admin, row.Role);
        Assert.Equal(UserAccountStatus.Active, row.Status);
        Assert.Equal("bootstrap", row.DecidedBy);
    }

    [Fact]
    public async Task Legacy_entra_role_wins_over_lower_table_role()
    {
        await using var f = new LegacyAdminFactory();
        await Seed(f, new UserAccount { ObjectId = "dev-user", Email = "dev.user@example.com", DisplayName = "x", Role = AppRole.Viewer, Status = UserAccountStatus.Active });
        var client = f.CreateClient(NoRedirect);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Admin/Users")).StatusCode);
    }

    [Fact]
    public async Task Admin_approves_changes_role_disables_and_cannot_demote_self()
    {
        var client = factory.CreateClient(NoRedirect);
        var tag = Guid.NewGuid().ToString("N")[..8];

        // Add by e-mail.
        var add = await PostFormAsync(client, "/Admin/Users/Create", "/Admin/Users/Create",
            new() { ["Email"] = $"new.{tag}@example.com", ["DisplayName"] = $"New {tag}", ["Role"] = "Viewer" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        var dup = await PostFormAsync(client, "/Admin/Users/Create", "/Admin/Users/Create",
            new() { ["Email"] = $"NEW.{tag}@example.com", ["Role"] = "Viewer" });
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Contains("already exists", await dup.Content.ReadAsStringAsync());

        int addedId, pendingId, selfId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            addedId = (await db.UserAccounts.SingleAsync(u => u.Email == $"new.{tag}@example.com")).Id;
            var pending = new UserAccount { ObjectId = $"pend-{tag}", Email = $"pend.{tag}@example.com", DisplayName = $"Pending {tag}", Status = UserAccountStatus.Pending, CreatedAt = DateTimeOffset.UtcNow };
            db.UserAccounts.Add(pending);
            await db.SaveChangesAsync();
            pendingId = pending.Id;
            selfId = (await db.UserAccounts.SingleAsync(u => u.ObjectId == "dev-user")).Id;
        }

        var list = await client.GetStringAsync("/Admin/Users");
        Assert.Contains("1 pending", list);
        Assert.Contains("not yet signed in", list);
        Assert.Contains(list.Split('\n'), l => l.Contains("dev.user@example.com"));

        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/Users", $"/Admin/Users/Approve/{pendingId}", new() { ["role"] = "User" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/Users", $"/Admin/Users/ChangeRole/{addedId}", new() { ["role"] = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/Users", $"/Admin/Users/Disable/{addedId}", new())).StatusCode);

        var selfDemote = await PostFormAsync(client, "/Admin/Users", $"/Admin/Users/ChangeRole/{selfId}", new() { ["role"] = "Viewer" });
        Assert.Equal(HttpStatusCode.Redirect, selfDemote.StatusCode);
        Assert.Contains("cannot remove your own Admin access", await client.GetStringAsync("/Admin/Users"));
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(client, "/Admin/Users", $"/Admin/Users/Delete/{selfId}", new())).StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var approved = await db.UserAccounts.SingleAsync(u => u.Id == pendingId);
            Assert.Equal((AppRole.User, UserAccountStatus.Active, "dev-user"), (approved.Role, approved.Status, approved.DecidedBy));
            var added = await db.UserAccounts.SingleAsync(u => u.Id == addedId);
            Assert.Equal((AppRole.Admin, UserAccountStatus.Disabled), (added.Role, added.Status));
            var self = await db.UserAccounts.SingleAsync(u => u.Id == selfId);
            Assert.Equal((AppRole.Admin, UserAccountStatus.Active), (self.Role, self.Status));
            Assert.True(await db.AuditEvents.AnyAsync(e => e.Entity == "UserAccount" && e.EntityId == pendingId.ToString() && e.Action == "Update"));
        }

        // Directory resolves ids to names; the audit page shows the admin by name, not GUID only.
        var audit = await client.GetStringAsync("/Audit?entity=UserAccount");
        Assert.Contains("Dev User (dev.user@example.com)", audit);
    }

    [Fact]
    public async Task Sign_out_is_in_the_nav_and_lands_on_signed_out_page()
    {
        var client = factory.CreateClient(NoRedirect);
        var home = await client.GetStringAsync("/");
        Assert.Contains("action=\"/Access/SignOut\"", home);
        Assert.Contains("Sign out", home);

        var post = await PostFormAsync(client, "/", "/Access/SignOut", new());
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Equal("/Access/SignedOut", post.Headers.Location!.ToString());

        // Unknown users can sign out too: the page is outside the access gate.
        await using var f = new NoRoleFactory();
        var gated = f.CreateClient(NoRedirect);
        Assert.Equal(HttpStatusCode.Redirect, (await PostFormAsync(gated, "/Access", "/Access/SignOut", new())).StatusCode);
        Assert.Contains("signed out", await gated.GetStringAsync("/Access/SignedOut"));
    }

    private static async Task Seed(WebAppFactory f, UserAccount account)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        account.CreatedAt = DateTimeOffset.UtcNow;
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string tokenPage, string postUrl, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(tokenPage);
        fields["__RequestVerificationToken"] = TokenRegex.Match(html).Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}

/// <summary>Dev user carries only the legacy "Administrator" Entra app role.</summary>
public class LegacyAdminFactory : WebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Auth:Dev:Roles:0", "Administrator");
    }
}
