using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// A pooler's team inside a league. Stored at leagues/{leagueId}/teams/{username}:
/// the document id is the owner's normalized username (one team per user per league).
/// </summary>
[FirestoreData]
public sealed class Team
{
    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("ownerUsername")]
    public string OwnerUsername { get; set; } = "";

    /// <summary>NHL player ids on the roster.</summary>
    [FirestoreProperty("playerIds")]
    public List<long> PlayerIds { get; set; } = [];

    /// <summary>
    /// The NHL franchise this team carries as its identity (e.g. "COL"), when
    /// the league uses them. In "Les Mordus" every GM owns one franchise for
    /// life -- it is never traded and costs no cap.
    ///
    /// Deliberately a field here rather than a roster spot: a spot that could
    /// hold either a player or a whole team would make every roster, lineup
    /// and trade code path polymorphic for one permanent, untradeable case.
    /// Its points are derived from the `games` collection instead.
    /// </summary>
    [FirestoreProperty("franchiseAbbrev")]
    public string? FranchiseAbbrev { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }

    // --- computed by the scoring job / roster endpoints ---

    /// <summary>Displayed score: rawTopXScore + adjustmentsTotal.</summary>
    [FirestoreProperty("score")]
    public double Score { get; set; }

    /// <summary>Sum of the top-X counted players' season fantasy points.</summary>
    [FirestoreProperty("rawTopXScore")]
    public double RawTopXScore { get; set; }

    /// <summary>Sum of the transaction adjustment ledger (subcollection `adjustments`).</summary>
    [FirestoreProperty("adjustmentsTotal")]
    public double AdjustmentsTotal { get; set; }

    /// <summary>Players currently counting toward the score ("compteDansLesPoints").</summary>
    [FirestoreProperty("countedPlayerIds")]
    public List<long> CountedPlayerIds { get; set; } = [];

    /// <summary>Season fantasy points per rostered player (map keys are playerIds as strings).</summary>
    [FirestoreProperty("playerPoints")]
    public Dictionary<string, double> PlayerPoints { get; set; } = [];

    // --- team-level display fields, precomputed nightly so league-detail can
    // stay a light read (no per-player season-stats / PlayerCache lookups). ---

    /// <summary>Sum of games played across the roster (drives ptsPerGame = score / this).</summary>
    [FirestoreProperty("rosterGamesPlayed")]
    public int RosterGamesPlayed { get; set; }

    /// <summary>Sum of the roster's cap hits (0 for players with no salary).</summary>
    [FirestoreProperty("capTotal")]
    public long CapTotal { get; set; }

    /// <summary>NHL points (goals + assists) per rostered player — for player ranking in Trades/NewsTicker. Keys are playerIds as strings.</summary>
    [FirestoreProperty("playerNhlPoints")]
    public Dictionary<string, int> PlayerNhlPoints { get; set; } = [];

    [FirestoreProperty("scoreUpdatedUtc")]
    public Timestamp? ScoreUpdatedUtc { get; set; }

    // --- weekly-lineup scoring (written by PeriodRollupJob) ---
    //
    // Points are banked per period: once a week is finalized its contribution
    // never moves again, which is what makes trades free of retroactive effects
    // and removes the need for the adjustment ledger entirely.
    //
    // Invariant: score == FinalizedScore + PeriodPoints.

    /// <summary>
    /// Guards double-counting when a period is banked. Written in the same
    /// document update as <see cref="FinalizedScore"/> so the two can never
    /// disagree, which makes a re-run a no-op instead of a doubled total.
    /// </summary>
    [FirestoreProperty("finalizedThroughPeriodIndex")]
    public int FinalizedThroughPeriodIndex { get; set; }

    /// <summary>Points banked from every finalized period. Never recomputed from scratch.</summary>
    [FirestoreProperty("finalizedScore")]
    public double FinalizedScore { get; set; }

    /// <summary>The current period's active points, recomputed from scratch nightly.</summary>
    [FirestoreProperty("periodPoints")]
    public double PeriodPoints { get; set; }

    /// <summary>What the current period's benched players produced — "points left on the bench".</summary>
    [FirestoreProperty("benchScore")]
    public double BenchScore { get; set; }

    [FirestoreProperty("currentPeriodIndex")]
    public int CurrentPeriodIndex { get; set; }

    /// <summary>
    /// Active points per period index (~28 entries a season). Keeps a weekly
    /// history chart free of extra reads.
    /// </summary>
    [FirestoreProperty("periodScores")]
    public Dictionary<string, double> PeriodScores { get; set; } = [];
}
