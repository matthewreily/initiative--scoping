using System.Security.Claims;
using System.Text.Encodings.Web;
using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.Extensions.Options;

namespace InitiativeScoping.Web.Authorization;

public static class AuthSetup
{
    public const string DevScheme = "DevAuth";

    public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        var useDevAuth = config.GetValue<bool>("Auth:UseDevelopmentAuth") && !env.IsProduction();

        if (useDevAuth)
        {
            services.AddAuthentication(DevScheme)
                .AddScheme<DevAuthOptions, DevAuthHandler>(DevScheme, o =>
                {
                    o.UserId = config["Auth:Dev:UserId"] ?? "dev-user";
                    o.DisplayName = config["Auth:Dev:DisplayName"] ?? "Dev User";
                    o.Email = config["Auth:Dev:Email"];
                    o.Roles = config.GetSection("Auth:Dev:Roles").Get<string[]>() ?? [AppRoles.Admin];
                });
        }
        else
        {
            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"));
            services.AddRazorPages().AddMicrosoftIdentityUI();
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.Admin, p => p.RequireRole(AppRoles.Admin))
            .AddPolicy(AppPolicies.CanEdit, p => p.RequireRole(AppRoles.Admin, AppRoles.User))
            .AddPolicy(AppPolicies.CanView, p => p.RequireRole(AppRoles.All))
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddMemoryCache();
        services.AddHttpContextAccessor();
        services.AddSingleton<AccessCache>();
        services.AddScoped<IClaimsTransformation, AppClaimsTransformation>();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}

public class DevAuthOptions : AuthenticationSchemeOptions
{
    public string UserId { get; set; } = "dev-user";
    public string DisplayName { get; set; } = "Dev User";
    public string? Email { get; set; }
    public string[] Roles { get; set; } = [];
}

/// <summary>Local-development only: authenticates every request as a configured user with configured roles.</summary>
public class DevAuthHandler(IOptionsMonitor<DevAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<DevAuthOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Options.UserId),
            new(ClaimTypes.Name, Options.DisplayName)
        };
        if (!string.IsNullOrWhiteSpace(Options.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, Options.Email));
        }

        claims.AddRange(Options.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string UserId => User is null ? "anonymous" : PrincipalClaims.ObjectId(User);

    public string DisplayName => User is null ? "Anonymous" : PrincipalClaims.DisplayName(User);

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;
}

/// <summary>Reads the identity claims the app relies on, tolerant of the different claim names Entra and the dev scheme emit.</summary>
public static class PrincipalClaims
{
    public static string ObjectId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimConstants.ObjectId)
        ?? user.FindFirstValue(ClaimConstants.Oid)
        ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "anonymous";

    public static string DisplayName(ClaimsPrincipal user) =>
        user.Identity?.Name ?? user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? "Anonymous";

    public static string? Email(ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimConstants.PreferredUserName)
            ?? user.FindFirstValue(ClaimTypes.Email)
            ?? user.FindFirstValue("email")
            ?? user.FindFirstValue(ClaimTypes.Upn)
            ?? user.FindFirstValue("upn");
        return string.IsNullOrWhiteSpace(value) || !value.Contains('@') ? null : value.Trim();
    }
}
