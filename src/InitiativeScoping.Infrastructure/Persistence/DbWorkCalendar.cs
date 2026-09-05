using InitiativeScoping.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public sealed class DbWorkCalendar(AppDbContext db) : IWorkCalendar
{
    public async Task<WorkCalendar> GetAsync(CancellationToken ct = default)
    {
        var hoursPerDay = await db.WorkCalendarSettings
            .Select(s => (decimal?)s.HoursPerDay)
            .FirstOrDefaultAsync(ct) ?? WorkCalendar.DefaultHoursPerDay;
        var holidays = await db.Holidays.Select(h => h.Date).ToListAsync(ct);
        return new WorkCalendar(hoursPerDay, holidays.ToHashSet());
    }
}
