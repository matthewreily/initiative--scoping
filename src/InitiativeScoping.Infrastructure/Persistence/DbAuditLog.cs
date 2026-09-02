using System.Text.Json;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;

namespace InitiativeScoping.Infrastructure.Persistence;

public class DbAuditLog(AppDbContext db, ICurrentUser currentUser, TimeProvider clock) : IAuditLog
{
    public void Record(string entity, object entityId, string action, object? diff = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Entity = entity,
            EntityId = entityId.ToString() ?? string.Empty,
            Action = action,
            UserId = currentUser.UserId,
            At = clock.GetUtcNow(),
            DiffJson = diff is null ? null : JsonSerializer.Serialize(diff)
        });
    }
}
