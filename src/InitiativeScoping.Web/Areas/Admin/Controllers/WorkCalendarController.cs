using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class WorkCalendarController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var settings = await db.WorkCalendarSettings.FirstOrDefaultAsync(ct);
        var holidays = await db.Holidays.OrderBy(h => h.Date).ToListAsync(ct);
        return View(new WorkCalendarViewModel
        {
            HoursPerDay = settings?.HoursPerDay ?? WorkCalendar.DefaultHoursPerDay,
            Holidays = holidays
        });
    }

    [HttpPost]
    public async Task<IActionResult> Settings(WorkCalendarViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            model.Holidays = await db.Holidays.OrderBy(h => h.Date).ToListAsync(ct);
            return View("Index", model);
        }

        var settings = await db.WorkCalendarSettings.FirstOrDefaultAsync(ct);
        var before = settings?.HoursPerDay ?? WorkCalendar.DefaultHoursPerDay;
        if (settings is null)
        {
            settings = new WorkCalendarSettings();
            db.WorkCalendarSettings.Add(settings);
        }

        settings.HoursPerDay = model.HoursPerDay;
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(WorkCalendarSettings), settings.Id, AuditActions.Update, new { Before = before, After = settings.HoursPerDay });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Hours per working day set to {settings.HoursPerDay:0.##}.");
    }

    public IActionResult CreateHoliday() => View("EditHoliday", new HolidayEditModel());

    [HttpPost]
    public async Task<IActionResult> CreateHoliday(HolidayEditModel model, CancellationToken ct)
    {
        await ValidateUniqueDate(model, ct);
        if (!ModelState.IsValid)
        {
            return View("EditHoliday", model);
        }

        var holiday = new Holiday { Date = model.Date!.Value, Name = model.Name.Trim() };
        db.Holidays.Add(holiday);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Holiday), holiday.Id, AuditActions.Create, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Holiday '{holiday.Name}' ({holiday.Date:yyyy-MM-dd}) added.");
    }

    public async Task<IActionResult> EditHoliday(int id, CancellationToken ct)
    {
        var holiday = await db.Holidays.FindAsync([id], ct);
        if (holiday is null)
        {
            return NotFound();
        }

        return View(new HolidayEditModel { Id = holiday.Id, Date = holiday.Date, Name = holiday.Name });
    }

    [HttpPost]
    public async Task<IActionResult> EditHoliday(int id, HolidayEditModel model, CancellationToken ct)
    {
        var holiday = await db.Holidays.FindAsync([id], ct);
        if (holiday is null)
        {
            return NotFound();
        }

        model.Id = id;
        await ValidateUniqueDate(model, ct);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var before = new { holiday.Date, holiday.Name };
        holiday.Date = model.Date!.Value;
        holiday.Name = model.Name.Trim();
        audit.Record(nameof(Holiday), holiday.Id, AuditActions.Update, new { Before = before, After = new { holiday.Date, holiday.Name } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Holiday '{holiday.Name}' updated.");
    }

    [HttpPost]
    public async Task<IActionResult> DeleteHoliday(int id, CancellationToken ct)
    {
        var holiday = await db.Holidays.FindAsync([id], ct);
        if (holiday is null)
        {
            return NotFound();
        }

        db.Holidays.Remove(holiday);
        audit.Record(nameof(Holiday), holiday.Id, AuditActions.Delete, new { holiday.Date, holiday.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess($"Holiday '{holiday.Name}' deleted.");
    }

    private async Task ValidateUniqueDate(HolidayEditModel model, CancellationToken ct)
    {
        if (model.Date is null)
        {
            return;
        }

        if (model.Date.Value.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            ModelState.AddModelError(nameof(model.Date), "Weekends are already non-working days; choose a weekday.");
        }

        if (await db.Holidays.AnyAsync(h => h.Id != model.Id && h.Date == model.Date.Value, ct))
        {
            ModelState.AddModelError(nameof(model.Date), "A holiday already exists on this date.");
        }
    }
}
