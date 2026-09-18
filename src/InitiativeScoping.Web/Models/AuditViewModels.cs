using InitiativeScoping.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace InitiativeScoping.Web.Models;

public class AuditIndexModel
{
    public required IReadOnlyList<AuditEvent> Events { get; init; }
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public string? Entity { get; init; }
    public string? EntityId { get; init; }
    public string? Action { get; init; }
    public string? UserId { get; init; }
    public required IReadOnlyList<string> Entities { get; init; }
    public required IReadOnlyList<string> Actions { get; init; }
    public int PageCount => Paging.PageCount(Total, PageSize);

    public PagerModel Pager(IUrlHelper url) => new(Page, PageSize, Total, (page, size) =>
        url.Action("Index", "Audit", new { entity = Entity, entityId = EntityId, act = Action, userId = UserId, page, size })!);
}
