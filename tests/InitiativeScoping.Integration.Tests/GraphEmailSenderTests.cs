using System.Net;
using System.Text;
using System.Text.Json;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Web.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace InitiativeScoping.Integration.Tests;

public class GraphEmailSenderTests
{
    private static readonly EmailMessage Message = new(["a@example.com", "b@example.com"], "New access request", "Body text");

    [Fact]
    public async Task Disabled_without_sender_mailbox_or_credentials_and_drops_message()
    {
        var calls = 0;
        var noSender = Sender(_ => { calls++; throw new InvalidOperationException("no call expected"); }, sender: null);
        var noCreds = Sender(_ => { calls++; throw new InvalidOperationException("no call expected"); }, clientSecret: null);

        Assert.False(noSender.IsEnabled);
        Assert.False(noCreds.IsEnabled);
        await noSender.SendAsync(Message, CancellationToken.None);
        await noCreds.SendAsync(Message, CancellationToken.None);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Sends_via_graph_sendMail_from_configured_mailbox()
    {
        HttpRequestMessage? sendRequest = null;
        string? body = null;
        var sender = AsyncSender(async req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/token"))
            {
                return Json("""{"access_token":"tok","expires_in":3600}""");
            }

            sendRequest = req;
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        Assert.True(sender.IsEnabled);
        await sender.SendAsync(Message, CancellationToken.None);

        Assert.NotNull(sendRequest);
        Assert.Equal(HttpMethod.Post, sendRequest!.Method);
        Assert.Equal("https://graph.microsoft.com/v1.0/users/noreply%40example.com/sendMail", sendRequest.RequestUri!.ToString());
        Assert.Equal("Bearer tok", sendRequest.Headers.Authorization!.ToString());

        using var doc = JsonDocument.Parse(body!);
        var message = doc.RootElement.GetProperty("message");
        Assert.Equal("New access request", message.GetProperty("subject").GetString());
        Assert.Equal("Text", message.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal("Body text", message.GetProperty("body").GetProperty("content").GetString());
        Assert.Equal(["a@example.com", "b@example.com"],
            message.GetProperty("toRecipients").EnumerateArray().Select(r => r.GetProperty("emailAddress").GetProperty("address").GetString()).ToArray());
        Assert.False(doc.RootElement.GetProperty("saveToSentItems").GetBoolean());
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Graph_errors_are_swallowed(HttpStatusCode status)
    {
        var sender = Sender(req => req.RequestUri!.AbsolutePath.EndsWith("/token")
            ? Json("""{"access_token":"tok","expires_in":3600}""")
            : new HttpResponseMessage(status) { Content = new StringContent("{}") });
        await sender.SendAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task Transport_failure_is_swallowed()
    {
        var sender = Sender(_ => throw new HttpRequestException("boom"));
        await sender.SendAsync(Message, CancellationToken.None);
    }

    [Fact]
    public async Task No_recipients_makes_no_call()
    {
        var sender = Sender(_ => throw new InvalidOperationException("no call expected"));
        await sender.SendAsync(new EmailMessage([], "s", "b"), CancellationToken.None);
    }

    private static GraphEmailSender Sender(Func<HttpRequestMessage, HttpResponseMessage> handler, string? sender = "noreply@example.com", string? clientSecret = "secret") =>
        AsyncSender(req => Task.FromResult(handler(req)), sender, clientSecret);

    private static GraphEmailSender AsyncSender(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler, string? sender = "noreply@example.com", string? clientSecret = "secret")
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:TenantId"] = "tenant",
            ["AzureAd:ClientId"] = "client",
            ["AzureAd:ClientSecret"] = clientSecret,
            [GraphEmailSender.SenderKey] = sender
        }).Build();
        return new GraphEmailSender(new HttpClient(new StubHandler(handler)), config, new GraphAppToken(config), NullLogger<GraphEmailSender>.Instance);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request);
    }
}
