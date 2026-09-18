using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Application.Abstractions;

/// <summary>Snapshot of the admin-managed work calendar used for fixed-duration hour calculations, fiscal reporting and labor Capex defaults.</summary>
public sealed record WorkCalendar(decimal HoursPerDay, IReadOnlySet<DateOnly> Holidays, int FiscalYearStartMonth = 1, decimal InternalCapexPercent = WorkCalendar.DefaultInternalCapexPercent, decimal VendorCapexPercent = WorkCalendar.DefaultVendorCapexPercent)
{
    public const decimal DefaultHoursPerDay = 8m;
    public const decimal DefaultInternalCapexPercent = 70m;
    public const decimal DefaultVendorCapexPercent = 100m;

    public FiscalCalendar Fiscal => new(FiscalYearStartMonth);

    /// <summary>Capex % a new labor allocation of the given class starts with; the user can still override it per line.</summary>
    public decimal DefaultCapexPercent(ResourcingClass resourcingClass) => resourcingClass == ResourcingClass.Vendor ? VendorCapexPercent : InternalCapexPercent;
}

public interface IWorkCalendar
{
    Task<WorkCalendar> GetAsync(CancellationToken ct = default);
}
