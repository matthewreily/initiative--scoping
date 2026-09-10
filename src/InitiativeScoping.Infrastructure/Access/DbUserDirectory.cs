using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InitiativeScoping.Infrastructure.Access;

/// <summary>Snapshot of <c>UserAccounts</c> held in memory (the table is small) and rebuilt on change or every minute.</summary>
public class DbUserDirectory(IServiceScopeFactory scopes, TimeProvider clock) : IUserDirectory
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(1);
    private readonly object gate = new();
    private Snapshot? snapshot;

    public string Display(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return "—";
        }

        return Current().ById.TryGetValue(userId, out var u) ? Format(u) : userId;
    }

    public IReadOnlyList<UserSummary> ActiveUsers() => Current().Active;

    public void Invalidate()
    {
        lock (gate)
        {
            snapshot = null;
        }
    }

    private static string Format(UserSummary u) =>
        string.IsNullOrWhiteSpace(u.Email) || string.Equals(u.Email, u.UserId, StringComparison.OrdinalIgnoreCase)
            ? u.DisplayName
            : $"{u.DisplayName} ({u.Email})";

    private Snapshot Current()
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            if (snapshot is not null && now - snapshot.LoadedAt < MaxAge)
            {
                return snapshot;
            }
        }

        var loaded = Load(now);
        lock (gate)
        {
            snapshot = loaded;
        }

        return loaded;
    }

    private Snapshot Load(DateTimeOffset now)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.UserAccounts.AsNoTracking()
            .Select(a => new { a.ObjectId, a.Email, a.DisplayName, a.Status })
            .ToList();

        var byId = new Dictionary<string, UserSummary>(StringComparer.OrdinalIgnoreCase);
        var active = new List<UserSummary>();
        foreach (var r in rows)
        {
            var summary = new UserSummary(r.ObjectId ?? r.Email, r.DisplayName, r.Email);
            byId[summary.UserId] = summary;
            byId.TryAdd(r.Email, summary);
            if (r.Status == UserAccountStatus.Active)
            {
                active.Add(summary);
            }
        }

        return new Snapshot(byId, active.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(), now);
    }

    private sealed record Snapshot(IReadOnlyDictionary<string, UserSummary> ById, IReadOnlyList<UserSummary> Active, DateTimeOffset LoadedAt);
}
