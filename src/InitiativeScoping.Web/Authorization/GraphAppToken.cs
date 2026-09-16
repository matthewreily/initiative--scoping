using System.Text.Json;

namespace InitiativeScoping.Web.Authorization;

/// <summary>
/// Client-credentials token for Microsoft Graph using the app's own registration
/// (<c>AzureAd:TenantId/ClientId/ClientSecret</c>), cached process-wide until shortly before expiry.
/// </summary>
public class GraphAppToken(IConfiguration config)
{
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static readonly TimeSpan TokenSkew = TimeSpan.FromMinutes(2);
    private static (string Value, DateTimeOffset ExpiresAt)? token;

    private readonly string? tenantId = config["AzureAd:TenantId"];
    private readonly string? clientId = config["AzureAd:ClientId"];
    private readonly string? clientSecret = config["AzureAd:ClientSecret"];
    private readonly string instance = (config["AzureAd:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(tenantId)
        && !string.IsNullOrWhiteSpace(clientId)
        && !string.IsNullOrWhiteSpace(clientSecret);

    public async Task<string> GetTokenAsync(HttpClient http, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        if (token is { } cached && cached.ExpiresAt - TokenSkew > now)
        {
            return cached.Value;
        }

        await TokenGate.WaitAsync(ct);
        try
        {
            if (token is { } again && again.ExpiresAt - TokenSkew > now)
            {
                return again.Value;
            }

            using var response = await http.PostAsync(
                $"{instance}/{tenantId}/oauth2/v2.0/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId!,
                    ["client_secret"] = clientSecret!,
                    ["scope"] = "https://graph.microsoft.com/.default",
                    ["grant_type"] = "client_credentials"
                }),
                ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var value = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("Token response had no access_token.");
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) && exp.TryGetInt32(out var seconds) ? seconds : 3600;
            token = (value, now.AddSeconds(expiresIn));
            return value;
        }
        finally
        {
            TokenGate.Release();
        }
    }
}
