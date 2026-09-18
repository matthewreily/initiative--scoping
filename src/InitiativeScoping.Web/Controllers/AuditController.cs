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
    private const int DefaultPageSize = 50;

    public async Task<IActionResult> Index(string? entity, string? entityId, string? act, string? userId, int page = 1, int? size = null, CancellationToken ct = default)
    {
        var pageSize = Paging.NormalizeSize(size, DefaultPageSize);
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
        page = Paging.ClampPage(page, total, pageSize);
        var events = await query.OrderByDescending(e => e.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var entities = await db.AuditEvents.Select(e => e.Entity).Distinct().ToListAsync(ct);
        var actions = await db.AuditEvents.Select(e => e.Action).Distinct().ToListAsync(ct);

        return View(new AuditIndexModel
        {
            Events = events, Total = total, Page = page, PageSize = pageSize,
            Entity = entity, EntityId = entityId, Action = act, UserId = userId,
            Entities = entities.Order().ToList(), Actions = actions.Order().ToList()
        });
    }
}
