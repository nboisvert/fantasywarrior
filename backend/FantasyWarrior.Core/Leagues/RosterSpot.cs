using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// One stint of a player belonging to a team. Collection:
/// leagues/{leagueId}/rosterSpots.
///
/// This is the renamed <c>Assignment</c>. The rename is not cosmetic: in the
/// weekly-lineup model "assignment" comes to mean a player's activation for a
/// given week, which is a different thing entirely. Keeping one word for both
/// would make the code unreadable exactly where it needs to be clearest.
///
/// Spots are never deleted, only closed — a team keeps the points a player
/// banked for it even after he is traded away, so the closed spot stays part
/// of the team's total forever.
///
/// Field names changed too (<c>from</c>/<c>to</c> became
/// <c>startDate</c>/<c>endDate</c>), deliberately: it forces any code reading
/// these documents to be updated explicitly rather than silently
/// reinterpreting legacy ones.
/// </summary>
[FirestoreData]
public sealed class RosterSpot
{
    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

    [FirestoreProperty("teamUsername")]
    public string TeamUsername { get; set; } = "";

    /// <summary>Raw NHL position code, denormalized when the spot opens.</summary>
    [FirestoreProperty("position")]
    public string Position { get; set; } = "";

    /// <summary>
    /// F, D or G, denormalized so lineup validation needs no player lookup.
    /// Intentionally frozen at open: a player's slot eligibility stays stable
    /// for the life of the spot even if the NHL relists his position.
    /// </summary>
    [FirestoreProperty("positionGroup")]
    public string PositionGroup { get; set; } = "";

    /// <summary>Inclusive ET date the stint began.</summary>
    [FirestoreProperty("startDate")]
    public string StartDate { get; set; } = "";

    /// <summary>draft | trade | freeagent</summary>
    [FirestoreProperty("startReason")]
    public string StartReason { get; set; } = RosterSpotReason.FreeAgent;

    [FirestoreProperty("startRefId")]
    public string? StartRefId { get; set; }

    /// <summary>Inclusive ET date the stint ended; null means still held.</summary>
    [FirestoreProperty("endDate")]
    public string? EndDate { get; set; }

    /// <summary>trade | release</summary>
    [FirestoreProperty("endReason")]
    public string? EndReason { get; set; }

    [FirestoreProperty("endRefId")]
    public string? EndRefId { get; set; }

    [FirestoreProperty("openedUtc")]
    public Timestamp OpenedUtc { get; set; }

    [FirestoreProperty("closedUtc")]
    public Timestamp? ClosedUtc { get; set; }

    // --- rollups, written by the scoring job ---

    /// <summary>
    /// Guards double-counting: a period is folded into the finalized totals
    /// only once. Written in the same update as the totals it guards, so the
    /// two can never disagree.
    /// </summary>
    [FirestoreProperty("finalizedThroughPeriodIndex")]
    public int FinalizedThroughPeriodIndex { get; set; }

    [FirestoreProperty("finalizedActivePoints")]
    public double FinalizedActivePoints { get; set; }

    [FirestoreProperty("finalizedBenchPoints")]
    public double FinalizedBenchPoints { get; set; }

    /// <summary>finalized + the current period, recomputed from scratch nightly.</summary>
    [FirestoreProperty("activePoints")]
    public double ActivePoints { get; set; }

    [FirestoreProperty("benchPoints")]
    public double BenchPoints { get; set; }

    [FirestoreProperty("activeStats")]
    public Dictionary<string, object>? ActiveStats { get; set; }

    /// <summary>Games played while active — the honest denominator for points per game.</summary>
    [FirestoreProperty("activeGamesPlayed")]
    public int ActiveGamesPlayed { get; set; }

    [FirestoreProperty("rollupUpdatedUtc")]
    public Timestamp? RollupUpdatedUtc { get; set; }

    public bool IsOpen => EndDate is null;

    public StatLine Stats() => StatLine.FromMap(ActiveStats);
}

public static class RosterSpotReason
{
    public const string Draft = "draft";
    public const string Trade = "trade";
    public const string FreeAgent = "freeagent";
    public const string Release = "release";
}
