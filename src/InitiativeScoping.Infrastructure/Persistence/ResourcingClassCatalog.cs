using InitiativeScoping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public sealed record ResourcingClassOption(int Id, string Name);

/// <summary>Select-list helpers for the admin-managed resourcing class catalog.</summary>
public static class ResourcingClassCatalog
{
    /// <summary>Active classes in catalog order, plus any inactive classes in <paramref name="includeIds"/> so an existing record keeps its class selectable.</summary>
    public static async Task<IReadOnlyList<ResourcingClassOption>> OptionsAsync(AppDbContext db, IEnumerable<int>? includeIds, CancellationToken ct)
    {
        var ids = (includeIds ?? []).ToList();
        return (await db.ResourcingClasses.Where(c => c.IsActive || ids.Contains(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct))
            .Select(c => new ResourcingClassOption(c.Id, c.IsActive ? c.Name : c.Name + " (inactive)")).ToList();
    }

    /// <summary>All classes for CSV parsing (inactive included so historic exports re-import).</summary>
    public static Task<List<ResourcingClass>> AllAsync(AppDbContext db, CancellationToken ct) =>
        db.ResourcingClasses.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
}
