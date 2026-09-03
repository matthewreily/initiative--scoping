using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Application.Abstractions;

public sealed record ExternalTimeEntry(
    string SourceReference,
    string ExternalProjectId,
    string? ExternalPersonId,
    DateOnly WorkDate,
    decimal Hours,
    decimal? Cost);

/// <summary>
/// Pluggable actuals connector. Planview ships in v1; Jira is a fast-follow implementation with no schema change.
/// </summary>
public interface IActualsSource
{
    string SourceKey { get; }

    Task<IReadOnlyList<ExternalTimeEntry>> FetchAsync(
        InitiativeSourceMapping mapping,
        DateOnly since,
        CancellationToken cancellationToken);
}
