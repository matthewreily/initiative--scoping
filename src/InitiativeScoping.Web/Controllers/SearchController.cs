using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

[Authorize(Policy = AppPolicies.CanView)]
public class SearchController(AppDbContext db, ICurrentUser currentUser) : Controller
{
    private const int MaxPerGroup = 25;

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var term = (q ?? "").Trim();
        var model = new SearchResultsModel { Query = term };
        if (term.Length < 2)
        {
            return View(model);
        }

        var lowered = term.ToLower();
        var isAdmin = currentUser.IsInRole(AppRoles.Admin);

        model.Initiatives = (await db.Initiatives
            .Where(i => i.ScenarioOfId == null)
            .Where(i => i.Name.ToLower().Contains(lowered) || (i.Description != null && i.Description.ToLower().Contains(lowered)))
            .OrderBy(i => i.Name)
            .Take(MaxPerGroup)
            .Select(i => new { i.Id, i.Name, BusinessUnit = i.BusinessUnit!.Name, i.Status })
            .ToListAsync(ct))
            .Select(i => new SearchHit(i.Id, i.Name, $"{i.BusinessUnit} · {i.Status}"))
            .ToList();

        if (isAdmin)
        {
            model.People = (await db.People
                .Where(p => p.DisplayName.ToLower().Contains(lowered) || (p.ExternalIds != null && p.ExternalIds.ToLower().Contains(lowered)))
                .OrderBy(p => p.DisplayName)
                .Take(MaxPerGroup)
                .Select(p => new { p.Id, p.DisplayName, ResourceType = p.ResourceType!.Name, BusinessUnit = p.BusinessUnit!.Name, p.IsActive })
                .ToListAsync(ct))
                .Select(p => new SearchHit(p.Id, p.DisplayName, $"{p.ResourceType} · {p.BusinessUnit}{(p.IsActive ? "" : " · inactive")}"))
                .ToList();

            model.RateCards = (await db.RateCards
                .Where(r => r.Name.ToLower().Contains(lowered))
                .OrderByDescending(r => r.EffectiveStart)
                .Take(MaxPerGroup)
                .Select(r => new { r.Id, r.Name, r.Status, r.EffectiveStart })
                .ToListAsync(ct))
                .Select(r => new SearchHit(r.Id, r.Name, $"{r.Status} · effective {r.EffectiveStart:yyyy-MM-dd}"))
                .ToList();

            model.Vendors = (await db.Vendors
                .Where(v => v.Name.ToLower().Contains(lowered))
                .OrderBy(v => v.Name)
                .Take(MaxPerGroup)
                .Select(v => new { v.Id, v.Name, v.IsActive })
                .ToListAsync(ct))
                .Select(v => new SearchHit(v.Id, v.Name, v.IsActive ? "Active" : "Inactive"))
                .ToList();
        }

        return View(model);
    }
}
