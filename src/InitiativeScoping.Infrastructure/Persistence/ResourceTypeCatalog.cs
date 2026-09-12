using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

/// <summary>A CSV row's resource type name plus the discipline it named (null when the file has no Discipline column or left it blank).</summary>
public sealed record ResourceTypeRequest(string ResourceType, string? Discipline);

public sealed record ResourceTypeCatalogResult(
    Dictionary<string, ResourceType> Types,
    IReadOnlyList<string> AddedTypes,
    IReadOnlyList<string> AddedDisciplines);

public static class ImportCatalogNote
{
    /// <summary>Success-message suffix listing catalog values an import created.</summary>
    public static string For(IReadOnlyList<string> addedLevels, ResourceTypeCatalogResult resourceTypes)
    {
        var parts = new List<string>();
        if (addedLevels.Count > 0)
        {
            parts.Add($" New seniority level(s) added to the catalog: {string.Join(", ", addedLevels)}.");
        }
        if (resourceTypes.AddedTypes.Count > 0)
        {
            parts.Add($" New resource type(s) added to the catalog: {string.Join(", ", resourceTypes.AddedTypes)}.");
        }
        if (resourceTypes.AddedDisciplines.Count > 0)
        {
            parts.Add($" New discipline(s) added to the catalog: {string.Join(", ", resourceTypes.AddedDisciplines)}.");
        }
        return string.Concat(parts);
    }
}

/// <summary>
/// Name → <see cref="ResourceType"/> lookup for CSV imports. Unknown resource types are added (audited); a new type takes the
/// row's Discipline (created if unknown) or the shared "Unassigned" discipline when none is given. Known types keep their discipline.
/// </summary>
public static class ResourceTypeCatalog
{
    public static async Task<ResourceTypeCatalogResult> ResolveOrCreateAsync(
        AppDbContext db, IAuditLog audit, IEnumerable<ResourceTypeRequest> requests, CancellationToken ct)
    {
        var types = (await db.ResourceTypes.ToListAsync(ct))
            .GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var disciplines = (await db.Disciplines.ToListAsync(ct))
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var addedTypes = new List<string>();
        var addedDisciplines = new List<string>();

        foreach (var request in requests)
        {
            var name = request.ResourceType.Trim();
            if (name.Length == 0 || types.ContainsKey(name))
            {
                continue;
            }

            var disciplineName = string.IsNullOrWhiteSpace(request.Discipline) ? Discipline.UnassignedName : request.Discipline.Trim();
            if (!disciplines.TryGetValue(disciplineName, out var discipline))
            {
                discipline = new Discipline { Name = disciplineName };
                db.Disciplines.Add(discipline);
                disciplines[disciplineName] = discipline;
                addedDisciplines.Add(disciplineName);
            }

            var type = new ResourceType { Name = name, Discipline = discipline };
            db.ResourceTypes.Add(type);
            types[name] = type;
            addedTypes.Add(name);
        }

        if (addedTypes.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            foreach (var name in addedDisciplines)
            {
                var discipline = disciplines[name];
                audit.Record(nameof(Discipline), discipline.Id, AuditActions.Create, new { discipline.Name, Source = "CSV import" });
            }
            foreach (var name in addedTypes)
            {
                var type = types[name];
                audit.Record(nameof(ResourceType), type.Id, AuditActions.Create, new { type.Name, Discipline = type.Discipline!.Name, Source = "CSV import" });
            }
        }

        return new ResourceTypeCatalogResult(types, addedTypes, addedDisciplines);
    }
}
