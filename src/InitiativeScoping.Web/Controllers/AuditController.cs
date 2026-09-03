using InitiativeScoping.Application;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

[Authorize(Policy = AppPolicies.CanView)]
public class AuditController(AppDbContext db) : Controller
{
    private const int PageSize = 50;

    public async Task<IActionResult> Index(string? entity, string? entityId, string? act, string? userId, int page = 1, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entity))
        {
            query = query.Where(e => e.Entity == entity);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(act))
        {
            query = query.Where(e => e.Action == act);
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(e => e.UserId == userId);
        }

        var total = await query.CountAsync(ct);
        var events = await query.OrderByDescending(e => e.Id).Skip((page - 1) * PageSize).Take(PageSize).ToListAsync(ct);
        var entities = await db.AuditEvents.Select(e => e.Entity).Distinct().ToListAsync(ct);
        var actions = await db.AuditEvents.Select(e => e.Action).Distinct().ToListAsync(ct);

        return View(new AuditIndexModel
        {
            Events = events, Total = total, Page = page, PageSize = PageSize,
            Entity = entity, EntityId = entityId, Action = act, UserId = userId,
            Entities = entities.Order().ToList(), Actions = actions.Order().ToList()
        });
    }
}
