namespace InitiativeScoping.Application.Abstractions;

/// <summary>Snapshot of the admin-managed work calendar used for fixed-duration hour calculations.</summary>
public sealed record WorkCalendar(decimal HoursPerDay, IReadOnlySet<DateOnly> Holidays)
{
    public const decimal DefaultHoursPerDay = 8m;
}

public interface IWorkCalendar
{
    Task<WorkCalendar> GetAsync(CancellationToken ct = default);
}
