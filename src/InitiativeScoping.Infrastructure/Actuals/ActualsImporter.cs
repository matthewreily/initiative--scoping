using System.Text;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Actuals;

public class ActualsImporter(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock) : IActualsImporter
{
    public async Task<ActualsImport> ImportAsync(string source, IReadOnlyList<ExternalTimeEntry> entries, string? fileName, CancellationToken ct)
    {
        var import = new ActualsImport
        {
            Source = source,
            StartedAt = clock.GetUtcNow(),
            StartedBy = currentUser.UserId,
            FileName = fileName,
            Status = ActualsImportStatus.Completed
        };
        db.ActualsImports.Add(import);

        var mappings = await db.InitiativeSourceMappings
            .Where(m => m.Source == source)
            .ToDictionaryAsync(m => m.ExternalProjectId, m => m.InitiativeId, StringComparer.OrdinalIgnoreCase, ct);
        var people = await db.People.Where(p => p.IsActive).ToListAsync(ct);
        var cards = await LoadRateCardsAsync(ct);

        var references = entries.Select(e => e.SourceReference).ToList();
        var existing = await db.ActualEntries
            .Where(e => e.ActualsImport!.Source == source && references.Contains(e.SourceReference))
            .Select(e => e.SourceReference)
            .ToListAsync(ct);
        var seen = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);

        var log = new StringBuilder();
        foreach (var e in entries)
        {
            if (!seen.Add(e.SourceReference))
            {
                import.SkippedCount++;
                log.AppendLine($"Skipped '{e.SourceReference}': already imported.");
                continue;
            }

            var entry = new ActualEntry
            {
                ExternalProjectId = e.ExternalProjectId,
                ExternalPersonId = e.ExternalPersonId,
                WorkDate = e.WorkDate,
                Hours = e.Hours,
                SourcedCost = e.Cost,
                SourceReference = e.SourceReference
            };

            if (mappings.TryGetValue(e.ExternalProjectId, out var initiativeId))
            {
                entry.InitiativeId = initiativeId;
            }
            else
            {
                log.AppendLine($"'{e.SourceReference}': no initiative mapped to project '{e.ExternalProjectId}'.");
            }

            var person = people.FirstOrDefault(p => ActualsCosting.MatchesExternalId(p, e.ExternalPersonId));
            if (person is not null)
            {
                entry.PersonId = person.Id;
            }
            else
            {
                log.AppendLine($"'{e.SourceReference}': no roster person for '{e.ExternalPersonId ?? "(blank)"}'.");
            }

            Apply(entry, person, cards);
            if (entry.IsUnmapped)
            {
                import.UnmappedCount++;
            }

            import.RecordCount++;
            import.Entries.Add(entry);
        }

        import.Status = import.UnmappedCount > 0 ? ActualsImportStatus.CompletedWithUnmapped : ActualsImportStatus.Completed;
        import.FinishedAt = clock.GetUtcNow();
        import.Log = log.Length == 0 ? null : log.ToString();

        await db.SaveChangesAsync(ct);
        audit.Record(nameof(ActualsImport), import.Id, AuditActions.Import,
            new { import.Source, import.FileName, import.RecordCount, import.UnmappedCount, import.SkippedCount });
        await db.SaveChangesAsync(ct);
        return import;
    }

    public async Task RemapAsync(ActualEntry entry, int? initiativeId, int? personId, CancellationToken ct)
    {
        var before = new { entry.InitiativeId, entry.PersonId, entry.CalculatedCost };
        if (initiativeId is not null)
        {
            entry.InitiativeId = initiativeId;
        }

        if (personId is not null)
        {
            entry.PersonId = personId;
        }

        var person = entry.PersonId is null ? null : await db.People.FindAsync([entry.PersonId.Value], ct);
        Apply(entry, person, await LoadRateCardsAsync(ct));
        audit.Record(nameof(ActualEntry), entry.Id, AuditActions.Remap,
            new { Before = before, After = new { entry.InitiativeId, entry.PersonId, entry.CalculatedCost } });
    }

    private static void Apply(ActualEntry entry, Person? person, List<RateCard> cards)
    {
        entry.IsUnmapped = entry.InitiativeId is null || entry.PersonId is null;
        entry.CalculatedCost = ActualsCosting.Calculate(entry, person, cards);
    }

    private Task<List<RateCard>> LoadRateCardsAsync(CancellationToken ct) =>
        db.RateCards.Include(c => c.Entries).Where(c => c.Status == RateCardStatus.Published).AsNoTracking().ToListAsync(ct);
}
