using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

public class Person
{
    public int Id { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>Semicolon-separated identifiers used by external sources (Planview resource id, e-mail, Jira account id…).</summary>
    public string? ExternalIds { get; set; }
    public bool IsActive { get; set; } = true;
    public int ResourceTypeId { get; set; }
    public ResourceType? ResourceType { get; set; }
    public Seniority Seniority { get; set; }
    public required string Location { get; set; }
    public ResourcingClass ResourcingClass { get; set; }
    public int BusinessUnitId { get; set; }
    public BusinessUnit? BusinessUnit { get; set; }
}

public class InitiativeSourceMapping
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public required string Source { get; set; }
    public required string ExternalProjectId { get; set; }
}

public class ActualsImport
{
    public int Id { get; set; }
    public required string Source { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required string Status { get; set; }
    public required string StartedBy { get; set; }
    public string? FileName { get; set; }
    public int RecordCount { get; set; }
    public int UnmappedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? Log { get; set; }
    public List<ActualEntry> Entries { get; set; } = [];
}

public class ActualEntry
{
    public int Id { get; set; }
    public int ActualsImportId { get; set; }
    public ActualsImport? ActualsImport { get; set; }
    public int? InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public int? PersonId { get; set; }
    public Person? Person { get; set; }
    public required string ExternalProjectId { get; set; }
    public string? ExternalPersonId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public decimal? SourcedCost { get; set; }
    public decimal? CalculatedCost { get; set; }
    public required string SourceReference { get; set; }
    public bool IsUnmapped { get; set; }

    /// <summary>Cost reported by the source if present, otherwise hours × the roster-resolved rate.</summary>
    public decimal? EffectiveCost => SourcedCost ?? CalculatedCost;
}

public static class ActualsSources
{
    public const string Csv = "Csv";
    public const string Planview = "Planview";
    public const string Jira = "Jira";
    public static readonly string[] All = [Csv, Planview, Jira];
}

public static class ActualsImportStatus
{
    public const string Completed = "Completed";
    public const string CompletedWithUnmapped = "CompletedWithUnmapped";
    public const string Failed = "Failed";
}

public class ActualAdjustment
{
    public int Id { get; set; }
    public int InitiativeId { get; set; }
    public Initiative? Initiative { get; set; }
    public decimal Hours { get; set; }
    public decimal Cost { get; set; }
    public required string Reason { get; set; }
    public required string CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class AuditEvent
{
    public long Id { get; set; }
    public required string Entity { get; set; }
    public required string EntityId { get; set; }
    public required string Action { get; set; }
    public required string UserId { get; set; }
    public DateTimeOffset At { get; set; }
    public string? DiffJson { get; set; }
}
