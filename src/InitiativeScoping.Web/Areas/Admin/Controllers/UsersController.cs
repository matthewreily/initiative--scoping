using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class UsersController(AppDbContext db, IAuditLog audit, ICurrentUser currentUser, IUserDirectory directory, AccessCache accessCache, TimeProvider clock)
    : AdminControllerBase
{
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var query = db.UserAccounts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(term) || u.DisplayName.ToLower().Contains(term) || (u.ObjectId != null && u.ObjectId.Contains(term)));
        }

        var users = await query
            .OrderBy(u => u.Status == UserAccountStatus.Pending ? 0 : u.Status == UserAccountStatus.Active ? 1 : 2)
            .ThenBy(u => u.DisplayName)
            .ToListAsync(ct);
        return View(new UsersIndexModel { Query = q, Users = users, CurrentUserId = currentUser.UserId });
    }

    public IActionResult Create() => View(new UserAccountEditModel { Role = AppRole.User });

    [HttpPost]
    public async Task<IActionResult> Create(UserAccountEditModel model, CancellationToken ct)
    {
        var email = model.Email.Trim();
        if (await db.UserAccounts.AnyAsync(u => u.Email == email, ct))
        {
            ModelState.AddModelError(nameof(model.Email), "A user with this e-mail already exists.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var now = clock.GetUtcNow();
        var account = new UserAccount
        {
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? email : model.DisplayName.Trim(),
            Role = model.Role,
            Status = UserAccountStatus.Active,
            CreatedAt = now,
            DecidedAt = now,
            DecidedBy = currentUser.UserId,
            Note = string.IsNullOrWhiteSpace(model.Note) ? null : model.Note.Trim()
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(UserAccount), account.Id, AuditActions.Create, new { account.Email, Role = account.Role.ToString() });
        await db.SaveChangesAsync(ct);
        Changed();
        return RedirectWithSuccess($"{account.Email} added as {account.Role}. Their access starts at first sign-in.");
    }

    [HttpPost]
    public Task<IActionResult> Approve(int id, AppRole role, CancellationToken ct) =>
        Decide(id, role, UserAccountStatus.Active, "approved", ct);

    [HttpPost]
    public Task<IActionResult> Reject(int id, CancellationToken ct) =>
        Decide(id, null, UserAccountStatus.Disabled, "rejected", ct);

    [HttpPost]
    public Task<IActionResult> Disable(int id, CancellationToken ct) =>
        Decide(id, null, UserAccountStatus.Disabled, "disabled", ct);

    [HttpPost]
    public Task<IActionResult> Enable(int id, CancellationToken ct) =>
        Decide(id, null, UserAccountStatus.Active, "re-enabled", ct);

    [HttpPost]
    public Task<IActionResult> ChangeRole(int id, AppRole role, CancellationToken ct) =>
        Decide(id, role, null, "updated", ct);

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var account = await db.UserAccounts.FindAsync([id], ct);
        if (account is null)
        {
            return NotFound();
        }

        if (IsSelf(account))
        {
            return RedirectWithError("You cannot remove your own account.");
        }

        db.UserAccounts.Remove(account);
        audit.Record(nameof(UserAccount), account.Id, AuditActions.Delete, new { account.Email, account.ObjectId });
        await db.SaveChangesAsync(ct);
        Changed();
        return RedirectWithSuccess($"{account.Email} removed.");
    }

    private async Task<IActionResult> Decide(int id, AppRole? role, UserAccountStatus? status, string verb, CancellationToken ct)
    {
        var account = await db.UserAccounts.FindAsync([id], ct);
        if (account is null)
        {
            return NotFound();
        }

        if (role is { } r && !Enum.IsDefined(r))
        {
            return RedirectWithError("Unknown role.");
        }

        var demotingSelf = IsSelf(account) && (status == UserAccountStatus.Disabled || (role is { } nr && nr != AppRole.Admin));
        if (demotingSelf)
        {
            return RedirectWithError("You cannot remove your own Admin access.");
        }

        var before = new { Role = account.Role.ToString(), Status = account.Status.ToString() };
        if (role is { } newRole)
        {
            account.Role = newRole;
        }

        if (status is { } newStatus)
        {
            account.Status = newStatus;
        }

        account.DecidedAt = clock.GetUtcNow();
        account.DecidedBy = currentUser.UserId;
        audit.Record(nameof(UserAccount), account.Id, AuditActions.Update,
            new { account.Email, Before = before, After = new { Role = account.Role.ToString(), Status = account.Status.ToString() } });
        await db.SaveChangesAsync(ct);
        Changed();
        return RedirectWithSuccess($"{account.DisplayName} {verb}: {account.Role}, {account.Status}.");
    }

    private bool IsSelf(UserAccount account) =>
        account.ObjectId is not null && string.Equals(account.ObjectId, currentUser.UserId, StringComparison.OrdinalIgnoreCase);

    private void Changed()
    {
        directory.Invalidate();
        accessCache.Invalidate();
    }
}

public class UsersIndexModel
{
    public string? Query { get; init; }
    public required IReadOnlyList<UserAccount> Users { get; init; }
    public required string CurrentUserId { get; init; }
    public int Pending => Users.Count(u => u.Status == UserAccountStatus.Pending);
}

public class UserAccountEditModel
{
    [Required, EmailAddress, StringLength(320)]
    [Display(Name = "E-mail")]
    public string Email { get; set; } = "";

    [StringLength(200)]
    [Display(Name = "Display name")]
    public string? DisplayName { get; set; }

    public AppRole Role { get; set; } = AppRole.User;

    [StringLength(1000)]
    public string? Note { get; set; }
}
