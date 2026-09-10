using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Access;

public class UserAccessOptions
{
    public const string Section = "Auth";

    /// <summary>Object ids or e-mails that are always Admin, so the first administrator can sign in before any row exists.</summary>
    public string[] BootstrapAdmins { get; set; } = [];
}

/// <summary>What a signed-in principal is allowed to do, and the account row (if any) behind it.</summary>
public record AccessResolution(AppRole? EffectiveRole, UserAccountStatus? AccountStatus, bool IsBootstrapAdmin)
{
    public bool HasAccess => EffectiveRole is not null;
}

public record SignedInUser(string ObjectId, string? Email, string DisplayName, IReadOnlyCollection<AppRole> TokenRoles);

/// <summary>
/// Combines the three role sources on sign-in: app roles carried in the token (legacy Entra assignment),
/// the configured bootstrap admins, and the Active <see cref="UserAccount"/> row. The highest wins.
/// Also keeps the account row's profile (name, e-mail, last seen) current so ids can be shown as names.
/// </summary>
public class UserAccessService(AppDbContext db, IUserDirectory directory, TimeProvider clock, Microsoft.Extensions.Options.IOptions<UserAccessOptions> options)
{
    private static readonly TimeSpan LastSeenGranularity = TimeSpan.FromMinutes(5);

    public async Task<AccessResolution> ResolveAsync(SignedInUser user, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var bootstrap = IsBootstrapAdmin(user);
        AppRole? tokenRole = user.TokenRoles.Count == 0 ? null : user.TokenRoles.Max();
        if (bootstrap)
        {
            tokenRole = AppRole.Admin;
        }

        var account = await FindAsync(user, ct);
        var changed = false;
        if (account is null)
        {
            if (tokenRole is null)
            {
                return new AccessResolution(null, null, false);
            }

            account = new UserAccount
            {
                ObjectId = user.ObjectId,
                Email = user.Email ?? user.ObjectId,
                DisplayName = user.DisplayName,
                Role = tokenRole.Value,
                Status = UserAccountStatus.Active,
                CreatedAt = now,
                LastSeenAt = now,
                DecidedAt = now,
                DecidedBy = bootstrap ? "bootstrap" : "entra-role",
                Note = bootstrap ? "Bootstrap admin (Auth:BootstrapAdmins)." : "Created from Entra app role at first sign-in."
            };
            db.UserAccounts.Add(account);
            changed = true;
        }
        else
        {
            if (account.ObjectId is null)
            {
                account.ObjectId = user.ObjectId;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(user.DisplayName) && account.DisplayName != user.DisplayName)
            {
                account.DisplayName = user.DisplayName;
                changed = true;
            }

            if (user.Email is not null && !string.Equals(account.Email, user.Email, StringComparison.OrdinalIgnoreCase)
                && !await db.UserAccounts.AnyAsync(a => a.Id != account.Id && a.Email == user.Email, ct))
            {
                account.Email = user.Email;
                changed = true;
            }

            if (account.LastSeenAt is null || now - account.LastSeenAt.Value >= LastSeenGranularity)
            {
                account.LastSeenAt = now;
                changed = true;
            }
        }

        if (changed)
        {
            await db.SaveChangesAsync(ct);
            directory.Invalidate();
        }

        var accountRole = account.Status == UserAccountStatus.Active ? account.Role : (AppRole?)null;
        AppRole? effective = (tokenRole, accountRole) switch
        {
            (null, null) => null,
            (null, var a) => a,
            (var t, null) => t,
            (var t, var a) => t > a ? t : a
        };
        return new AccessResolution(effective, account.Status, bootstrap);
    }

    /// <summary>Creates a Pending row for an authenticated user with no account; no-op when one exists.</summary>
    public async Task<UserAccount> RequestAccessAsync(SignedInUser user, string? note, CancellationToken ct)
    {
        var existing = await FindAsync(user, ct);
        if (existing is not null)
        {
            return existing;
        }

        var account = new UserAccount
        {
            ObjectId = user.ObjectId,
            Email = user.Email ?? user.ObjectId,
            DisplayName = user.DisplayName,
            Role = AppRole.Viewer,
            Status = UserAccountStatus.Pending,
            CreatedAt = clock.GetUtcNow(),
            LastSeenAt = clock.GetUtcNow(),
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        directory.Invalidate();
        return account;
    }

    public Task<UserAccount?> FindAsync(SignedInUser user, CancellationToken ct) =>
        db.UserAccounts.FirstOrDefaultAsync(
            a => a.ObjectId == user.ObjectId || (a.ObjectId == null && user.Email != null && a.Email == user.Email),
            ct);

    public bool IsBootstrapAdmin(SignedInUser user) =>
        options.Value.BootstrapAdmins.Any(b =>
            string.Equals(b, user.ObjectId, StringComparison.OrdinalIgnoreCase)
            || (user.Email is not null && string.Equals(b, user.Email, StringComparison.OrdinalIgnoreCase)));
}
