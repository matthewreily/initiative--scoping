using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Application.Abstractions;

/// <summary>
/// Persists a batch of external time entries as an <see cref="ActualsImport"/>: maps projects via
/// <see cref="InitiativeSourceMapping"/>, people via the roster, prices from rate cards, flags what it cannot map.
/// Shared by the CSV upload and every <see cref="IActualsSource"/> connector.
/// </summary>
public interface IActualsImporter
{
    Task<ActualsImport> ImportAsync(
        string source,
        IReadOnlyList<ExternalTimeEntry> entries,
        string? fileName,
        CancellationToken cancellationToken);

    /// <summary>Re-points an entry at an initiative and/or person and re-prices it. Null keeps the current value.</summary>
    Task RemapAsync(ActualEntry entry, int? initiativeId, int? personId, CancellationToken cancellationToken);
}
