using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class HomeChecklistTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public async Task Admin_home_shows_getting_started_checklist_and_shortcut_hooks()
    {
        var client = factory.CreateClient();
        var page = await client.GetStringAsync("/");
        Assert.Contains("Getting started", page);
        Assert.Contains("Published rate card", page);
        Assert.Contains("data-shortcut=\"g i\"", page);
        Assert.Contains("data-shortcut=\"g a\"", page);

        var list = await client.GetStringAsync("/Initiatives");
        Assert.Contains("data-columns", list);
        Assert.Contains("data-paginate=\"25\"", list);
        Assert.Contains("data-shortcut=\"n\"", list);
    }

    [Fact]
    public async Task Viewer_home_has_no_checklist_or_admin_shortcuts()
    {
        await using var f = new NoRoleFactory();
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserAccounts.Add(new UserAccount { ObjectId = "no-role-user", Email = "no.role@example.com", DisplayName = "x", Role = AppRole.Viewer, Status = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var page = await f.CreateClient().GetStringAsync("/");
        Assert.DoesNotContain("Getting started", page);
        Assert.DoesNotContain("data-shortcut=\"g a\"", page);
        Assert.Contains("data-shortcut=\"g i\"", page);
    }
}
