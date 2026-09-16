using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using InitiativeScoping.Application.Abstractions;

namespace InitiativeScoping.Web.Authorization;

/// <summary>
/// Sends mail through Microsoft Graph <c>/users/{sender}/sendMail</c> with the app's own credentials.
/// Requires the <c>Mail.Send</c> application permission with admin consent and a licensed mailbox
/// named in <c>Email:GraphSender</c>. Best-effort: failures are logged, never thrown.
/// </summary>
public class GraphEmailSender(HttpClient http, IConfiguration config, GraphAppToken appToken, ILogger<GraphEmailSender> logger) : IEmailSender
{
    public const string SenderKey = "Email:GraphSender";

    private readonly string? sender = config[SenderKey];

    public bool IsEnabled => !string.IsNullOrWhiteSpace(sender) && appToken.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (message.To.Count == 0)
        {
            return;
        }

        if (!IsEnabled)
        {
            logger.LogInformation("Graph e-mail not configured ({Key} / AzureAd); dropping \"{Subject}\" to {Recipients}", SenderKey, message.Subject, string.Join(", ", message.To));
            return;
        }

        try
        {
            var accessToken = await appToken.GetTokenAsync(http, ct);
            var payload = new
            {
                message = new
                {
                    subject = message.Subject,
                    body = new { contentType = "Text", content = message.TextBody },
                    toRecipients = message.To.Select(a => new { emailAddress = new { address = a } }).ToArray()
                },
                saveToSentItems = false
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender!)}/sendMail")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Graph sendMail denied ({Status}); grant Mail.Send (application) with admin consent and check that {Sender} is a licensed mailbox.", (int)response.StatusCode, sender);
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Graph sendMail failed ({Status}) for \"{Subject}\": {Body}", (int)response.StatusCode, message.Subject, await response.Content.ReadAsStringAsync(ct));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Graph sendMail failed for \"{Subject}\"", message.Subject);
        }
    }
}
