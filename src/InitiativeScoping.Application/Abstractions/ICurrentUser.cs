namespace InitiativeScoping.Application.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }
    string DisplayName { get; }
    bool IsInRole(string role);
}
