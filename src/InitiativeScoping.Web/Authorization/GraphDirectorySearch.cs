using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using InitiativeScoping.Application.Abstractions;

namespace InitiativeScoping.Web.Authorization;

public class GraphDirectoryOptions
{
    public const string Section = "DirectorySearch";

    /// <summary>Master switch; the search also needs AzureAd TenantId/ClientId/ClientSecret.</summary>
    public bool Enabled { get; set; } = true;

    public int MaxResults { get; set; } = 20;
}

/// <summary>
/// Searches tenant users through Microsoft Graph with the app's own credentials (client credentials flow).
/// Requires the <c>User.Read.All</c> application permission with admin consent on the Entra app; without it
/// the search reports an error and admins fall back to adding people by e-mail.
/// </summary>
public class GraphDirectorySearch(HttpClient http, IConfiguration config, GraphAppToken appToken, ILogger<GraphDirectorySearch> logger) : IDirectorySearch
{
    private readonly GraphDirectoryOptions options = config.GetSection(GraphDirectoryOptions.Section).Get<GraphDirectoryOptions>() ?? new();

    public bool IsAvailable => options.Enabled && appToken.IsConfigured;

    public async Task<DirectorySearchResult> SearchAsync(string term, CancellationToken ct)
    {
        term = term.Trim();
        if (!IsAvailable || term.Length < 2)
        {
            return DirectorySearchResult.Empty;
        }

        try
        {
            var accessToken = await appToken.GetTokenAsync(http, ct);
            var escaped = term.Replace("'", "''");
            var filter = $"accountEnabled eq true and (startswith(displayName,'{escaped}') or startswith(mail,'{escaped}') or startswith(userPrincipalName,'{escaped}') or startswith(givenName,'{escaped}') or startswith(surname,'{escaped}'))";
            var url = "https://graph.microsoft.com/v1.0/users"
                + $"?$filter={Uri.EscapeDataString(filter)}"
                + "&$select=id,displayName,mail,userPrincipalName,jobTitle"
                + $"&$top={Math.Clamp(options.MaxResults, 1, 100)}&$count=true&$orderby=displayName";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.Add("ConsistencyLevel", "eventual");
            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogWarning("Graph directory search denied ({Status}); grant User.Read.All (application) with admin consent.", (int)response.StatusCode);
                return new DirectorySearchResult([], "Directory search is not permitted for this app yet: grant the User.Read.All application permission (with admin consent) in Entra, or add the person by e-mail below.");
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var users = new List<DirectoryUser>();
            foreach (var u in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                var id = u.GetProperty("id").GetString();
                var upn = Str(u, "userPrincipalName");
                var mail = Str(u, "mail") ?? upn;
                if (id is null || mail is null)
                {
                    continue;
                }

                users.Add(new DirectoryUser(id, Str(u, "displayName") ?? mail, mail, Str(u, "jobTitle")));
            }

            return new DirectorySearchResult(users);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Graph directory search failed");
            return new DirectorySearchResult([], "Directory search is temporarily unavailable; add the person by e-mail below.");
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
