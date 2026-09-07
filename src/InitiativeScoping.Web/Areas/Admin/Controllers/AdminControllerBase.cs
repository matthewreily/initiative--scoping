using InitiativeScoping.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = AppPolicies.Admin)]
[AutoValidateAntiforgeryToken]
public abstract class AdminControllerBase : Controller
{
    protected IActionResult RedirectWithSuccess(string message, string action = "Index", object? routeValues = null)
    {
        TempData["Success"] = message;
        return RedirectToAction(action, routeValues);
    }

    protected IActionResult RedirectWithError(string message, string action = "Index", object? routeValues = null)
    {
        TempData["Error"] = message;
        return RedirectToAction(action, routeValues);
    }

    /// <summary>Loads the distinct selected rows; returns an error redirect when nothing valid was selected.</summary>
    protected static async Task<(List<T> Rows, string? Error)> LoadSelected<T>(IQueryable<T> set, int[] ids, Func<int[], System.Linq.Expressions.Expression<Func<T, bool>>> byIds, CancellationToken ct)
        where T : class
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length == 0)
        {
            return ([], "Select at least one row.");
        }

        var rows = await set.Where(byIds(distinct)).ToListAsync(ct);
        return rows.Count == 0 ? ([], "The selected rows no longer exist; refresh and try again.") : (rows, null);
    }

    /// <summary>Deletes the selected rows that pass <paramref name="canDelete"/>, skipping the rest, and reports a summary.</summary>
    protected async Task<IActionResult> BulkDeleteRows<T>(
        DbContext db,
        DbSet<T> set,
        int[] ids,
        Func<int[], System.Linq.Expressions.Expression<Func<T, bool>>> byIds,
        Func<T, CancellationToken, Task<bool>> canDelete,
        Func<T, string> name,
        Action<T> auditDelete,
        string singular,
        string plural,
        string skipReason,
        CancellationToken ct,
        string action = "Index",
        object? routeValues = null)
        where T : class
    {
        var (rows, error) = await LoadSelected(set, ids, byIds, ct);
        if (error is not null)
        {
            return RedirectWithError(error, action, routeValues);
        }

        var skipped = new List<string>();
        var deleted = 0;
        foreach (var row in rows)
        {
            if (!await canDelete(row, ct))
            {
                skipped.Add(name(row));
                continue;
            }

            set.Remove(row);
            auditDelete(row);
            deleted++;
        }

        await db.SaveChangesAsync(ct);
        return RedirectWithBulkSummary(deleted, $"{(deleted == 1 ? singular : plural)} deleted", skipped, skipReason, action, routeValues);
    }

    /// <summary>Sets IsActive on the selected rows, auditing each change.</summary>
    protected async Task<IActionResult> BulkSetActive<T>(
        DbContext db,
        DbSet<T> set,
        int[] ids,
        Func<int[], System.Linq.Expressions.Expression<Func<T, bool>>> byIds,
        Func<T, bool> isActive,
        Action<T, bool> setActive,
        Action<T, bool> auditChange,
        bool active,
        string singular,
        string plural,
        CancellationToken ct)
        where T : class
    {
        var (rows, error) = await LoadSelected(set, ids, byIds, ct);
        if (error is not null)
        {
            return RedirectWithError(error);
        }

        var changed = 0;
        foreach (var row in rows.Where(r => isActive(r) != active))
        {
            setActive(row, active);
            auditChange(row, active);
            changed++;
        }

        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"{changed} {(changed == 1 ? singular : plural)} {(active ? "activated" : "deactivated")}.");
    }

    /// <summary>"3 vendors deleted; 2 skipped (referenced)." style summary as success or error TempData.</summary>
    protected IActionResult RedirectWithBulkSummary(int done, string verb, IReadOnlyCollection<string> skipped, string skipReason, string action = "Index", object? routeValues = null)
    {
        var message = $"{done} {verb}";
        if (skipped.Count > 0)
        {
            message += $"; {skipped.Count} skipped ({skipReason}): {string.Join(", ", skipped.Take(5))}{(skipped.Count > 5 ? ", …" : "")}";
        }

        message += ".";
        return done > 0 ? RedirectWithSuccess(message, action, routeValues) : RedirectWithError(message, action, routeValues);
    }
}
