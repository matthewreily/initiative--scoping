using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InitiativeScoping.Integration.Tests;

public sealed class FakeDirectorySearch : IDirectorySearch
{
    public bool IsAvailable => true;
    public DirectorySearchResult Result { get; set; } = DirectorySearchResult.Empty;
    public string? LastTerm { get; private set; }

    public Task<DirectorySearchResult> SearchAsync(string term, CancellationToken ct)
    {
        LastTerm = term;
        return Task.FromResult(Result);
    }
}

public class DirectoryFactory : WebAppFactory
{
    public FakeDirectorySearch Directory { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(s => s.AddSingleton<IDirectorySearch>(Directory));
    }
}

public class DirectorySearchTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Dev_auth_has_no_directory_so_add_user_hides_search()
    {
        Assert.IsType<NoDirectorySearch>(factory.Services.GetRequiredService<IDirectorySearch>());
        var page = await factory.CreateClient().GetStringAsync("/Admin/Users/Create");
        Assert.DoesNotContain("Search the directory", page);
    }

    [Fact]
    public async Task Search_lists_matches_marks_existing_and_select_prefills_form_with_object_id()
    {
        await using var f = new DirectoryFactory();
        var tag = Guid.NewGuid().ToString("N")[..8];
        f.Directory.Result = new DirectorySearchResult(
        [
            new DirectoryUser($"oid-{tag}", $"Alex {tag}", $"alex.{tag}@example.com", "Analyst"),
            new DirectoryUser($"oid-dev-{tag}", "Dev User", "dev.user@example.com", null)
        ]);
        var client = f.CreateClient(NoRedirect);

        var page = await client.GetStringAsync($"/Admin/Users/Create?q=alex+{tag}");
        Assert.Equal($"alex {tag}", f.Directory.LastTerm);
        Assert.Contains($"Alex {tag}", page);
        Assert.Contains("Analyst", page);
        Assert.Contains("Already listed", page);
        Assert.Contains($"objectId=oid-{tag}", page);

        var prefilled = await client.GetStringAsync($"/Admin/Users/Create?email=alex.{tag}%40example.com&displayName=Alex+{tag}&objectId=oid-{tag}");
        Assert.Contains($"value=\"alex.{tag}@example.com\"", prefilled);
        Assert.Contains($"value=\"oid-{tag}\"", prefilled);

        var add = await PostFormAsync(client, "/Admin/Users/Create",
            new() { ["Email"] = $"alex.{tag}@example.com", ["DisplayName"] = $"Alex {tag}", ["ObjectId"] = $"oid-{tag}", ["Role"] = "User" });
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);

        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var account = await db.UserAccounts.SingleAsync(u => u.Email == $"alex.{tag}@example.com");
        Assert.Equal($"oid-{tag}", account.ObjectId);

        var dup = await PostFormAsync(client, "/Admin/Users/Create",
            new() { ["Email"] = $"other.{tag}@example.com", ["ObjectId"] = $"oid-{tag}", ["Role"] = "User" });
        Assert.Equal(HttpStatusCode.OK, dup.StatusCode);
        Assert.Contains("already exists", await dup.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Directory_error_is_shown_and_manual_form_remains()
    {
        await using var f = new DirectoryFactory();
        f.Directory.Result = new DirectorySearchResult([], "Directory search is not permitted for this app yet");
        var page = await f.CreateClient().GetStringAsync("/Admin/Users/Create?q=bob");
        Assert.Contains("not permitted", page);
        Assert.Contains("name=\"Email\"", page);
    }

    [Fact]
    public void Graph_search_is_unavailable_without_credentials()
    {
        var search = new GraphDirectorySearch(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("no call expected"))),
            Config(new() { ["AzureAd:TenantId"] = "t" }), NullLogger<GraphDirectorySearch>.Instance);
        Assert.False(search.IsAvailable);
    }

    [Fact]
    public async Task Graph_search_maps_users_and_uses_upn_when_mail_missing()
    {
        string? graphUrl = null;
        var search = Graph(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token"))
            {
                return Json("""{"access_token":"tok","expires_in":3600}""");
            }

            graphUrl = Uri.UnescapeDataString(req.RequestUri.ToString());
            Assert.Equal("Bearer tok", req.Headers.Authorization!.ToString());
            return Json("""
                {"value":[
                  {"id":"1","displayName":"Ann O'Neil","mail":"ann@example.com","userPrincipalName":"ann@example.onmicrosoft.com","jobTitle":"PM"},
                  {"id":"2","displayName":"Bob","mail":null,"userPrincipalName":"bob@example.com"}
                ]}
                """);
        });

        var result = await search.SearchAsync(" an ", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Collection(result.Users,
            u => { Assert.Equal("1", u.ObjectId); Assert.Equal("ann@example.com", u.Email); Assert.Equal("PM", u.JobTitle); },
            u => { Assert.Equal("bob@example.com", u.Email); Assert.Null(u.JobTitle); });
        Assert.Contains("accountEnabled eq true", graphUrl);
        Assert.Contains("startswith(displayName,'an')", graphUrl);
        Assert.Contains("$top=5", graphUrl);
    }

    [Fact]
    public async Task Graph_search_ignores_short_terms()
    {
        var search = Graph(_ => throw new InvalidOperationException("no call expected"));
        Assert.Empty((await search.SearchAsync("a", CancellationToken.None)).Users);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Graph_search_explains_missing_consent(HttpStatusCode status)
    {
        var search = Graph(req => req.RequestUri!.AbsolutePath.EndsWith("/token")
            ? Json("""{"access_token":"tok","expires_in":3600}""")
            : new HttpResponseMessage(status));

        var result = await search.SearchAsync("bob", CancellationToken.None);

        Assert.Empty(result.Users);
        Assert.Contains("User.Read.All", result.Error);
    }

    [Fact]
    public async Task Graph_search_degrades_on_transport_failure()
    {
        var search = Graph(_ => throw new HttpRequestException("boom"));
        var result = await search.SearchAsync("bob", CancellationToken.None);
        Assert.Empty(result.Users);
        Assert.Contains("temporarily unavailable", result.Error);
    }

    private static GraphDirectorySearch Graph(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new HttpClient(new StubHandler(handler)), Config(new()
        {
            ["AzureAd:TenantId"] = "tenant",
            ["AzureAd:ClientId"] = "client",
            ["AzureAd:ClientSecret"] = "secret",
            ["DirectorySearch:MaxResults"] = "5"
        }), NullLogger<GraphDirectorySearch>.Instance);

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string url, Dictionary<string, string> fields)
    {
        var html = await client.GetStringAsync(url);
        fields["__RequestVerificationToken"] = TokenRegex.Match(html).Groups[1].Value;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }
}
