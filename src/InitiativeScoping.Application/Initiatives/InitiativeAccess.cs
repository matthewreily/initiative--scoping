using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Application.Initiatives;

/// <summary>Per-initiative authorization on top of the role policies.</summary>
public static class InitiativeAccess
{
    public static bool CanCreate(ICurrentUser user) =>
        user.IsInRole(AppRoles.Admin) || user.IsInRole(AppRoles.User);

    /// <summary>Admins, or Users who are members with the Owner/Contributor role, may edit scope.</summary>
    public static bool CanEdit(ICurrentUser user, Initiative initiative) =>
        user.IsInRole(AppRoles.Admin) ||
        CanCreate(user) && initiative.Members.Any(m => m.UserId == user.UserId && m.Role is InitiativeMemberRole.Owner or InitiativeMemberRole.Contributor);

    /// <summary>Admins or initiative Owners manage members, delete, and change status.</summary>
    public static bool CanManage(ICurrentUser user, Initiative initiative) =>
        user.IsInRole(AppRoles.Admin) ||
        CanCreate(user) && initiative.Members.Any(m => m.UserId == user.UserId && m.Role == InitiativeMemberRole.Owner);

    /// <summary>Scope (phases/allocations/sizing) is editable in Draft, or on an Active initiative with an approved re-baseline in progress.</summary>
    public static bool IsScopeEditable(Initiative initiative) =>
        initiative.Status == InitiativeStatus.Draft ||
        initiative.Status == InitiativeStatus.Active && initiative.OpenRebaseline?.Status == RebaselineStatus.Approved;

    /// <summary>Named people on allocations (who fills a seat, not how many seats or hours) can also change while Active or On hold without a re-baseline.</summary>
    public static bool IsStaffingEditable(Initiative initiative) =>
        IsScopeEditable(initiative) || initiative.Status is InitiativeStatus.Active or InitiativeStatus.OnHold;

    /// <summary>Only Admins approve or reject re-baseline requests.</summary>
    public static bool CanApproveRebaseline(ICurrentUser user) => user.IsInRole(AppRoles.Admin);

    /// <summary>Only Admins activate directly or approve/reject activation requests; other managers request activation.</summary>
    public static bool CanApproveActivation(ICurrentUser user) => user.IsInRole(AppRoles.Admin);

    /// <summary>Admins and Users may add notes; Viewers read only.</summary>
    public static bool CanAddNote(ICurrentUser user) => CanCreate(user);

    /// <summary>Authors delete their own notes; Admins delete any.</summary>
    public static bool CanDeleteNote(ICurrentUser user, InitiativeNote note) =>
        user.IsInRole(AppRoles.Admin) || CanCreate(user) && note.CreatedBy == user.UserId;
}
