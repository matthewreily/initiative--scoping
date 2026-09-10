namespace InitiativeScoping.Application.Abstractions;

/// <summary>Resolves stored user ids (Entra object ids) to people, for display and pickers.</summary>
public interface IUserDirectory
{
    /// <summary>"Display Name (email)" for a known user; the raw id otherwise.</summary>
    string Display(string? userId);

    /// <summary>Active users, ordered by display name.</summary>
    IReadOnlyList<UserSummary> ActiveUsers();

    /// <summary>Drops cached data so the next call reflects admin changes.</summary>
    void Invalidate();
}

public record UserSummary(string UserId, string DisplayName, string Email);
