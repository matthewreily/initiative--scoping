using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Domain.Entities;

public class Person
{
    public int Id { get; set; }
    public required string DisplayName { get; set; }
    public string? ExternalIds { get; set; }
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
    public int RecordCount { get; set; }
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
    public DateOnly WorkDate { get; set; }
    public decimal Hours { get; set; }
    public decimal? SourcedCost { get; set; }
    public decimal? CalculatedCost { get; set; }
    public required string SourceReference { get; set; }
    public bool IsUnmapped { get; set; }
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
