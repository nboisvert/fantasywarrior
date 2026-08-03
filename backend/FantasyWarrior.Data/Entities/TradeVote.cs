namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One league member's "who won this trade" verdict. One vote per member per
/// trade, and permanent — re-voting is rejected, not an overwrite (2026-08-03,
/// per Nick).
///
/// Storing the favored *team* rather than a proposer-relative 1-5 rating is
/// what makes votes meaningful on their own: a future "which GM comes out ahead
/// most often" rollup can filter by team across every trade without needing to
/// know who proposed which one.
/// </summary>
public sealed class TradeVote
{
    public int TradeId { get; set; }

    public int UserId { get; set; }

    /// <summary>The team judged to have won; null means "fair".</summary>
    public int? FavoredTeamId { get; set; }

    public DateTime VotedUtc { get; set; }

    public Trade? Trade { get; set; }

    public User? User { get; set; }

    public Team? FavoredTeam { get; set; }
}
