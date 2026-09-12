using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

/// <summary>Name → <see cref="SeniorityLevel"/> lookup for CSV imports; unknown names are added to the catalog (audited) so vendor ladders import as-is.</summary>
public sealed record SeniorityOption(int Id, string Name);

public static class SeniorityCatalog
{
    public const string ConcurrentImportMessage =
        "Import not applied: the seniority or resource type catalog was changed by another import at the same time. Please retry the import.";

    /// <summary>Active levels plus any inactive ones in <paramref name="includeIds"/> (labelled "(inactive)") so edit forms keep the row's current value.</summary>
    public static async Task<IReadOnlyList<SeniorityOption>> OptionsAsync(AppDbContext db, IEnumerable<int> includeIds, CancellationToken ct)
    {
        var ids = includeIds.ToList();
        return (await db.SeniorityLevels.Where(s => s.IsActive || ids.Contains(s.Id)).OrderBy(s => s.SortOrder).ThenBy(s => s.Name).ToListAsync(ct))
            .Select(s => new SeniorityOption(s.Id, s.IsActive ? s.Name : s.Name + " (inactive)")).ToList();
    }

    public static async Task<(Dictionary<string, SeniorityLevel> Levels, IReadOnlyList<string> Added)> ResolveOrCreateAsync(
        AppDbContext db, IAuditLog audit, IEnumerable<string> names, CancellationToken ct)
    {
        var levels = (await db.SeniorityLevels.ToListAsync(ct))
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var nextOrder = levels.Values.Select(s => s.SortOrder).DefaultIfEmpty(0).Max();
        var added = new List<string>();

        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0 || levels.ContainsKey(name))
            {
                continue;
            }

            var level = new SeniorityLevel { Name = name, SortOrder = ++nextOrder };
            db.SeniorityLevels.Add(level);
            levels[name] = level;
            added.Add(name);
        }

        if (added.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var name in added)
            {
                var level = levels[name];
                audit.Record(nameof(SeniorityLevel), level.Id, AuditActions.Create, new { level.Name, level.SortOrder, Source = "CSV import" });
            }
        }

        return (levels, added);
    }
}
