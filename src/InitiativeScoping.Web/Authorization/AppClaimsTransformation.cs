using System.Security.Claims;
using InitiativeScoping.Application;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;

namespace InitiativeScoping.Web.Authorization;

public static class AppClaimTypes
{
    /// <summary>Marks a principal whose roles have already been resolved against the user table.</summary>
    public const string Resolved = "app:resolved";
    /// <summary>The <see cref="UserAccountStatus"/> of the signed-in user's account row, when one exists.</summary>
    public const string AccountStatus = "app:account-status";
}

/// <summary>Short-lived per-user cache of access resolutions; bumped whenever an Admin changes a user row.</summary>
public class AccessCache(IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);
    private int version;

    public void Invalidate() => Interlocked.Increment(ref version);

    public async Task<AccessResolution> GetOrAddAsync(string key, Func<Task<AccessResolution>> factory)
    {
        var cacheKey = $"access:v{Volatile.Read(ref version)}:{key}";
        if (cache.TryGetValue(cacheKey, out AccessResolution? cached) && cached is not null)
        {
            return cached;
        }

        var resolution = await factory();
        cache.Set(cacheKey, resolution, Ttl);
        return resolution;
    }
}

/// <summary>
/// Replaces the role claims Entra (or the dev scheme) issued with the single effective app role:
/// max(token app role, bootstrap admin, Active user-table role). Users without any role keep no role
/// claim and are sent to the request-access page by <see cref="AccessGateMiddleware"/>.
/// </summary>
public class AppClaimsTransformation(UserAccessService access, AccessCache cache, IHttpContextAccessor accessor) : IClaimsTransformation
{
    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated || identity.HasClaim(c => c.Type == AppClaimTypes.Resolved))
        {
            return principal;
        }

        var roleType = identity.RoleClaimType;
        var tokenRoles = identity.Claims
            .Where(c => c.Type == roleType || c.Type == ClaimTypes.Role || c.Type == "roles")
            .Select(c => AppRoles.Parse(c.Value))
            .OfType<AppRole>()
            .Distinct()
            .ToList();

        var user = new SignedInUser(PrincipalClaims.ObjectId(principal), PrincipalClaims.Email(principal), PrincipalClaims.DisplayName(principal), tokenRoles);
        var ct = accessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var key = $"{user.ObjectId}|{user.Email}|{user.DisplayName}|{string.Join(',', tokenRoles.OrderBy(r => r))}";
        var resolution = await cache.GetOrAddAsync(key, () => access.ResolveAsync(user, ct));

        var claims = identity.Claims
            .Where(c => c.Type != roleType && c.Type != ClaimTypes.Role && c.Type != "roles")
            .Select(c => new Claim(c.Type, c.Value, c.ValueType, c.Issuer, c.OriginalIssuer))
            .ToList();
        claims.Add(new Claim(AppClaimTypes.Resolved, "1"));
        if (resolution.AccountStatus is { } status)
        {
            claims.Add(new Claim(AppClaimTypes.AccountStatus, status.ToString()));
        }

        if (resolution.EffectiveRole is { } role)
        {
            claims.Add(new Claim(roleType, AppRoles.Name(role)));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, identity.AuthenticationType, identity.NameClaimType, roleType));
    }
}

/// <summary>Authenticated users with no app role can only reach the access-request page (and sign out).</summary>
public class AccessGateMiddleware(RequestDelegate next)
{
    private static readonly string[] OpenPrefixes = ["/Access", "/MicrosoftIdentity", "/signin-oidc", "/signout", "/health", "/Home/Error", "/Home/Status"];

    public Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true
            && !AppRoles.All.Any(user.IsInRole)
            && !OpenPrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect("/Access");
            return Task.CompletedTask;
        }

        return next(context);
    }
}
