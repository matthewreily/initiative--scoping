namespace InitiativeScoping.Application.Abstractions;

/// <summary>Looks people up in the identity provider's directory (e.g. Microsoft Graph) so admins can grant access before first sign-in.</summary>
public interface IDirectorySearch
{
    /// <summary>False when no directory is configured; callers fall back to manual e-mail entry.</summary>
    bool IsAvailable { get; }

    Task<DirectorySearchResult> SearchAsync(string term, CancellationToken ct);
}

public record DirectoryUser(string ObjectId, string DisplayName, string Email, string? JobTitle);

/// <param name="Error">Set when the directory could not be queried (missing consent, network); <paramref name="Users"/> is then empty.</param>
public record DirectorySearchResult(IReadOnlyList<DirectoryUser> Users, string? Error = null)
{
    public static readonly DirectorySearchResult Empty = new([]);
}

public sealed class NoDirectorySearch : IDirectorySearch
{
    public bool IsAvailable => false;

    public Task<DirectorySearchResult> SearchAsync(string term, CancellationToken ct) =>
        Task.FromResult(DirectorySearchResult.Empty);
}
