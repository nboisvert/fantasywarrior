namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A user's membership in a league. Replaces the Firestore
/// <c>memberUsernames</c> array, which existed only because an array-contains
/// query was the sole way to answer "which leagues am I in".
/// </summary>
public sealed class LeagueMember
{
    public int LeagueId { get; set; }

    public int UserId { get; set; }

    public DateTime JoinedUtc { get; set; }

    public League? League { get; set; }

    public User? User { get; set; }
}
