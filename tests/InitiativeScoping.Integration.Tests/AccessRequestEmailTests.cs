using System.Net;
using System.Text.RegularExpressions;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Integration.Tests;

public class RecordingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];
    public bool IsEnabled { get; set; } = true;

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

public class AccessRequestEmailTests
{
    private static readonly WebApplicationFactoryClientOptions NoRedirect = new() { AllowAutoRedirect = false };
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task New_access_request_emails_active_admins_and_bootstrap_admins_once()
    {
        var sender = new RecordingEmailSender();
        await using var f = new NoRoleFactory { BootstrapAdmins = ["boot@example.com", "00000000-0000-0000-0000-000000000001"] }
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IEmailSender>(sender)));
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserAccounts.AddRange(
                new UserAccount { Email = "admin@example.com", DisplayName = "Admin", Role = AppRole.Admin, Status = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow },
                new UserAccount { Email = "old@example.com", DisplayName = "Old admin", Role = AppRole.Admin, Status = UserAccountStatus.Disabled, CreatedAt = DateTimeOffset.UtcNow },
                new UserAccount { Email = "user@example.com", DisplayName = "User", Role = AppRole.User, Status = UserAccountStatus.Active, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var client = f.CreateClient(NoRedirect);
        var post = await PostFormAsync(client, "/Access", "/Access/Request", new() { ["Note"] = "Planning Q3" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);

        var mail = Assert.Single(sender.Sent);
        Assert.Equal(["admin@example.com", "boot@example.com"], mail.To.OrderBy(x => x));
        Assert.Contains("No Role", mail.Subject);
        Assert.Contains("no.role@example.com", mail.TextBody);
        Assert.Contains("Planning Q3", mail.TextBody);
        Assert.Contains("/Admin/Users", mail.TextBody);

        // Re-submitting does not create a second request or a second e-mail.
        post = await PostFormAsync(client, "/Access", "/Access/Request", new() { ["Note"] = "again" });
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Request_succeeds_without_mail_server()
    {
        await using var f = new NoRoleFactory();
        var client = f.CreateClient(NoRedirect);
        Assert.False(f.Services.GetRequiredService<IEmailSender>().IsEnabled);

        var post = await PostFormAsync(client, "/Access", "/Access/Request", new());
        Assert.Equal(HttpStatusCode.Redirect, post.StatusCode);
        Assert.Contains("waiting for an administrator", await client.GetStringAsync("/Access"));
    }

    private static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(getUrl);
        fields["__RequestVerificationToken"] = TokenRegex.Match(page).Groups[1].Value;
        return await client.PostAsync(postUrl, new FormUrlEncodedContent(fields));
    }
}
