using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Services;

public sealed record InitiativeActuals(IReadOnlyList<ActualEntry> Entries, IReadOnlyList<ActualAdjustment> Adjustments, VarianceResult Variance);

public static class ActualsQueries
{
    public const string DefaultThresholdKey = "Variance:DefaultThresholdPct";

    public static async Task<InitiativeActuals> LoadActualsAsync(this AppDbContext db, Initiative initiative, decimal? defaultThresholdPct, CancellationToken ct)
    {
        var entries = await db.ActualEntries
            .Include(e => e.Person)
            .Include(e => e.ActualsImport)
            .Where(e => e.InitiativeId == initiative.Id)
            .OrderByDescending(e => e.WorkDate).ThenBy(e => e.Id)
            .AsNoTracking()
            .ToListAsync(ct);
        var adjustments = await db.ActualAdjustments
            .Where(a => a.InitiativeId == initiative.Id)
            .OrderByDescending(a => a.Id)
            .AsNoTracking()
            .ToListAsync(ct);
        var typeNames = await db.ResourceTypes.ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var variance = VarianceCalculator.Calculate(initiative, entries, adjustments, typeNames, defaultThresholdPct);
        return new InitiativeActuals(entries, adjustments, variance);
    }
}
