using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Application.Abstractions;

/// <summary>Snapshot of the admin-managed work calendar used for fixed-duration hour calculations and fiscal reporting.</summary>
public sealed record WorkCalendar(decimal HoursPerDay, IReadOnlySet<DateOnly> Holidays, int FiscalYearStartMonth = 1)
{
    public const decimal DefaultHoursPerDay = 8m;

    public FiscalCalendar Fiscal => new(FiscalYearStartMonth);
}

public interface IWorkCalendar
{
    Task<WorkCalendar> GetAsync(CancellationToken ct = default);
}
