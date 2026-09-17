using System.Diagnostics;
using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

public class HomeController(AppDbContext db, ICurrentUser currentUser) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new HomeViewModel
        {
            UserDisplayName = currentUser.DisplayName,
            BusinessUnitCount = await db.BusinessUnits.CountAsync(ct),
            ResourceTypeCount = await db.ResourceTypes.CountAsync(ct),
            PublishedRateCardCount = await db.RateCards.CountAsync(r => r.Status == RateCardStatus.Published, ct),
            InitiativeCount = await db.Initiatives.CountAsync(ct),
            SetupSteps = currentUser.IsInRole(AppRoles.Admin) ? await SetupStepsAsync(ct) : []
        };
        return View(model);
    }

    private async Task<IReadOnlyList<SetupStep>> SetupStepsAsync(CancellationToken ct) =>
    [
        new("Business units", "Teams that sponsor and staff initiatives.", await db.BusinessUnits.AnyAsync(ct), "Admin", "BusinessUnits", "Index"),
        new("Resource types", "Roles that can be allocated, grouped by discipline.", await db.ResourceTypes.AnyAsync(ct), "Admin", "ResourceTypes", "Index"),
        new("Published rate card", "Hourly rates per resource type / seniority / class.", await db.RateCards.AnyAsync(r => r.Status == RateCardStatus.Published, ct), "Admin", "RateCards", "Index"),
        new("Allocation templates", "Default staffing per T-shirt size, used by Apply size.", await db.AllocationTemplates.AnyAsync(ct), "Admin", "Sizing", "Index"),
        new("Work calendar", "Hours per day and holidays for fixed-duration planning.", await db.WorkCalendarSettings.AnyAsync(ct), "Admin", "WorkCalendar", "Index"),
        new("Users", "Grant access to at least one other person.", await db.UserAccounts.AnyAsync(u => u.Status == UserAccountStatus.Active, ct), "Admin", "Users", "Index"),
        new("First initiative", "Create an initiative and apply a size.", await db.Initiatives.AnyAsync(ct), "", "Initiatives", "Create"),
    ];

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code;
        return View(new StatusViewModel(code, Activity.Current?.Id ?? HttpContext.TraceIdentifier));
    }
}
