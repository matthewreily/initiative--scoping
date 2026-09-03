using System.Diagnostics;
using InitiativeScoping.Application.Abstractions;
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
            PublishedRateCardCount = await db.RateCards.CountAsync(r => r.Status == Domain.Enums.RateCardStatus.Published, ct),
            InitiativeCount = await db.Initiatives.CountAsync(ct)
        };
        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
