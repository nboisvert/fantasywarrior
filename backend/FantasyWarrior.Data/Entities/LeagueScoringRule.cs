namespace FantasyWarrior.Data.Entities;

/// <summary>
/// What one stat is worth in one league — a row per scored stat, keyed by a
/// <c>StatKeys</c> name.
///
/// Rows rather than columns is the whole point: adding "1 point per blocked
/// shot" is an insert, not a migration. The API still assembles the five
/// historic values plus the rest into the shape the frontend already reads, so
/// this change is invisible to the UI.
///
/// A stat name outside <c>StatKeys.All</c> is rejected at the API rather than
/// stored: it would score zero forever and read as a calculation bug rather
/// than the typo it is.
/// </summary>
public sealed class LeagueScoringRule
{
    public int LeagueId { get; set; }

    /// <summary>A <c>StatKeys</c> name: goals, assists, wins, blockedShots…</summary>
    public required string StatKey { get; set; }

    public double PointValue { get; set; }

    public League? League { get; set; }
}
