using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class TeamUserPickerTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);
    private static readonly Regex DetailsRegex = new("/Initiatives/Details/(\\d+)", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Team_form_uses_the_picker_and_search_lists_app_users_then_directory_people()
    {
        await using var f = new DirectoryFactory();
        var tag = Guid.NewGuid().ToString("N")[..8];
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserAccounts.Add(new UserAccount { ObjectId = $"oid-{tag}", Email = $"casey.{tag}@example.com", DisplayName = $"Casey {tag}", Role = AppRole.User, Status = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            scope.ServiceProvider.GetRequiredService<IUserDirectory>().Invalidate();
        }

        f.Directory.Result = new DirectorySearchResult(
        [
            new DirectoryUser($"dir-{tag}", $"Dana {tag}", $"dana.{tag}@example.com", null),
            new DirectoryUser($"oid-{tag}", $"Casey {tag}", $"casey.{tag}@example.com", null)
        ]);

        var client = f.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(f, client);
        var details = await client.GetStringAsync($"/Initiatives/Details/{id}");
        Assert.Contains($"data-user-picker=\"/Initiatives/SearchUsers/{id}\"", details);
        Assert.DoesNotContain("user id / object id", details);
        Assert.Contains("search the company directory", details);

        var result = JsonSerializer.Deserialize<PickerResult>(await client.GetStringAsync($"/Initiatives/SearchUsers/{id}?q={tag}"), Json)!;
        Assert.True(result.DirectoryAvailable);
        Assert.Null(result.Error);
        Assert.Collection(result.Users,
            u => { Assert.Equal($"oid-{tag}", u.Id); Assert.Equal("app", u.Source); },
            u => { Assert.Equal($"dir-{tag}", u.Id); Assert.Equal("directory", u.Source); });

        // Directory person → member + Pending account so the Team tab shows a name, not an object id.
        var add = await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/AddMember/{id}", new()
        {
            ["UserId"] = $"dir-{tag}", ["DisplayName"] = $"Dana {tag}", ["Email"] = $"dana.{tag}@example.com", ["Role"] = nameof(InitiativeMemberRole.Contributor)
        });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        details = await client.GetStringAsync($"/Initiatives/Details/{id}");
        Assert.Contains($"Dana {tag} (dana.{tag}@example.com)", details);
        Assert.Contains("not signed in yet", details);
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var account = await db.UserAccounts.SingleAsync(a => a.ObjectId == $"dir-{tag}");
            Assert.Equal(UserAccountStatus.Pending, account.Status);
            Assert.Equal(AppRole.Viewer, account.Role);
        }

        // Members drop out of the picker; posting an empty UserId is rejected.
        result = JsonSerializer.Deserialize<PickerResult>(await client.GetStringAsync($"/Initiatives/SearchUsers/{id}?q=dana.{tag}"), Json)!;
        Assert.Empty(result.Users);
        var bad = await PostFormAsync(client, $"/Initiatives/Details/{id}", $"/Initiatives/AddMember/{id}", new() { ["UserId"] = "", ["Role"] = nameof(InitiativeMemberRole.Contributor) });
        Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
        Assert.Contains("Pick a user from the list", await client.GetStringAsync($"/Initiatives/Details/{id}"));
    }

    [Fact]
    public async Task Search_without_a_directory_lists_only_app_users()
    {
        await using var f = new WebAppFactory();
        var client = f.CreateClient(NoRedirect);
        var id = await CreateInitiativeAsync(f, client);
        var result = JsonSerializer.Deserialize<PickerResult>(await client.GetStringAsync($"/Initiatives/SearchUsers/{id}?q=zz-no-such-user"), Json)!;
        Assert.False(result.DirectoryAvailable);
        Assert.Empty(result.Users);
        Assert.DoesNotContain("search the company directory", await client.GetStringAsync($"/Initiatives/Details/{id}"));
    }

    private sealed record PickerOption(string Id, string Name, string Email, string Source);
    private sealed record PickerResult(List<PickerOption> Users, string? Error, bool DirectoryAvailable);

    private static async Task<int> CreateInitiativeAsync(WebAppFactory f, HttpClient client)
    {
        string buId;
        using (var scope = f.Services.CreateScope())
        {
            buId = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().BusinessUnits.FirstAsync(b => b.Name == "Boarding")).Id.ToString();
        }

        var response = await PostFormAsync(client, "/Initiatives/Create", "/Initiatives/Create", new()
        {
            ["Name"] = "Team picker", ["BusinessUnitId"] = buId, ["SizingMethod"] = nameof(SizingMethod.Direct), ["TargetStart"] = "2026-02-02"
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return int.Parse(DetailsRegex.Match(response.Headers.Location!.ToString()).Groups[1].Value);
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(getUrl);
        fields["__RequestVerificationToken"] = TokenRegex.Match(page).Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
