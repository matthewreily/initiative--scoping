using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;

namespace InitiativeScoping.Application.Initiatives;

/// <summary>Per-initiative authorization on top of the role policies.</summary>
public static class InitiativeAccess
{
    public static bool CanCreate(ICurrentUser user) =>
        user.IsInRole(AppRoles.Administrator) || user.IsInRole(AppRoles.InitiativeOwner) || user.IsInRole(AppRoles.Contributor);

    /// <summary>Administrators, or editing-role users who are members with the Owner/Contributor role, may edit scope.</summary>
    public static bool CanEdit(ICurrentUser user, Initiative initiative) =>
        user.IsInRole(AppRoles.Administrator) ||
        CanCreate(user) && initiative.Members.Any(m => m.UserId == user.UserId && m.Role is InitiativeMemberRole.Owner or InitiativeMemberRole.Contributor);

    /// <summary>Administrators or initiative Owners manage members, delete, and change status.</summary>
    public static bool CanManage(ICurrentUser user, Initiative initiative) =>
        user.IsInRole(AppRoles.Administrator) ||
        CanCreate(user) && initiative.Members.Any(m => m.UserId == user.UserId && m.Role == InitiativeMemberRole.Owner);

    /// <summary>Scope (phases/allocations/sizing) is editable in Draft, or on an Active initiative with an approved re-baseline in progress.</summary>
    public static bool IsScopeEditable(Initiative initiative) =>
        initiative.Status == InitiativeStatus.Draft ||
        initiative.Status == InitiativeStatus.Active && initiative.OpenRebaseline?.Status == RebaselineStatus.Approved;

    /// <summary>Only Administrators approve or reject re-baseline requests.</summary>
    public static bool CanApproveRebaseline(ICurrentUser user) => user.IsInRole(AppRoles.Administrator);
}
