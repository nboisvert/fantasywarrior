namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A person. One account, many leagues.
///
/// <see cref="Username"/> is the normalized (trimmed, lowercased) form and is
/// the app's public handle: every API route addresses a team by its owner's
/// username, which is what lets the SQL rebuild keep the existing frontend
/// contract unchanged.
/// </summary>
public sealed class User
{
    public int UserId { get; set; }

    /// <summary>Normalized: trimmed and lowercased. Unique.</summary>
    public required string Username { get; set; }

    /// <summary>As the user typed it, for display.</summary>
    public required string DisplayName { get; set; }

    /// <summary>
    /// Identity-provider subject id, for when real auth lands. Null today: the
    /// API still trusts the username the client sends, which is a known and
    /// documented gap, not an oversight.
    /// </summary>
    public string? ExternalAuthId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? LastLoginUtc { get; set; }

    public ICollection<LeagueMember> Memberships { get; set; } = [];

    public ICollection<Team> Teams { get; set; } = [];
}
