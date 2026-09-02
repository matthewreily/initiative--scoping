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
                    o.Roles = config.GetSection("Auth:Dev:Roles").Get<string[]>() ?? AppRoles.All;
                });
        }
        else
        {
            services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
                .AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"));
            services.AddRazorPages().AddMicrosoftIdentityUI();
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AppPolicies.Admin, p => p.RequireRole(AppRoles.Administrator))
            .AddPolicy(AppPolicies.CanEditInitiatives, p => p.RequireRole(AppRoles.Administrator, AppRoles.InitiativeOwner, AppRoles.Contributor))
            .AddPolicy(AppPolicies.CanView, p => p.RequireRole(AppRoles.All))
            .AddPolicy(AppPolicies.CanExport, p => p.RequireRole(AppRoles.Administrator, AppRoles.FinancePmo))
            .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        return services;
    }
}

public class DevAuthOptions : AuthenticationSchemeOptions
{
    public string UserId { get; set; } = "dev-user";
    public string DisplayName { get; set; } = "Dev User";
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
        claims.AddRange(Options.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string UserId =>
        User?.FindFirstValue(ClaimConstants.ObjectId)
        ?? User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? "anonymous";

    public string DisplayName => User?.Identity?.Name ?? User?.FindFirstValue("name") ?? "Anonymous";

    public bool IsInRole(string role) => User?.IsInRole(role) ?? false;
}
