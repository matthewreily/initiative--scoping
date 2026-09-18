using InitiativeScoping.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public sealed class DbWorkCalendar(AppDbContext db) : IWorkCalendar
{
    public async Task<WorkCalendar> GetAsync(CancellationToken ct = default)
    {
        var settings = await db.WorkCalendarSettings
            .Select(s => new { s.HoursPerDay, s.FiscalYearStartMonth })
            .FirstOrDefaultAsync(ct);
        var holidays = await db.Holidays.Select(h => h.Date).ToListAsync(ct);
        return new WorkCalendar(settings?.HoursPerDay ?? WorkCalendar.DefaultHoursPerDay, holidays.ToHashSet(), settings?.FiscalYearStartMonth ?? 1);
    }
}
