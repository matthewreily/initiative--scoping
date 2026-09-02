namespace InitiativeScoping.Application.Abstractions;

public interface IAuditLog
{
    /// <summary>Records an audit event for the current user. Saved with the caller's SaveChanges.</summary>
    void Record(string entity, object entityId, string action, object? diff = null);
}

public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string Publish = "Publish";
    public const string Retire = "Retire";
    public const string Import = "Import";
}
