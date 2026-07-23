using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// History of which team held which player over time.
/// Stored at leagues/{leagueId}/assignments/{auto-id}. Opened when a player
/// joins a roster, closed (To set) when he leaves. Audit trail — the score
/// itself is computed from season totals + the adjustment ledger.
/// </summary>
[FirestoreData]
public sealed class Assignment
{
    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

    [FirestoreProperty("teamUsername")]
    public string TeamUsername { get; set; } = "";

    /// <summary>First day (YYYY-MM-DD, inclusive) the player counts for the team.</summary>
    [FirestoreProperty("from")]
    public string From { get; set; } = "";

    /// <summary>Last day (YYYY-MM-DD, inclusive); null while the player is on the roster.</summary>
    [FirestoreProperty("to")]
    public string? To { get; set; }

    /// <summary>Set together with `to` — lets the activity feed order drop events.</summary>
    [FirestoreProperty("closedUtc")]
    public Timestamp? ClosedUtc { get; set; }

    /// <summary>How the player was acquired: draft | trade | freeagent (default).</summary>
    [FirestoreProperty("creationEvent")]
    public string CreationEvent { get; set; } = AssignmentCreationEvent.FreeAgent;

    /// <summary>Id of the creation event entity (trade id, draft pick id) when applicable.</summary>
    [FirestoreProperty("creationEventReferenceId")]
    public string? CreationEventReferenceId { get; set; }

    /// <summary>
    /// How this assignment ENDED — trade | release | null (still open, on
    /// the roster). Distinct from `creationEvent` (why it was OPENED): a
    /// freeagent-acquired assignment can still end via a trade, and the
    /// activity feed's drop event must reflect that, not the original
    /// acquisition reason. Null on legacy assignments closed before this
    /// field existed — the activity feed falls back to `creationEvent` for
    /// those.
    /// </summary>
    [FirestoreProperty("closeReason")]
    public string? CloseReason { get; set; }

    [FirestoreProperty("closeReasonReferenceId")]
    public string? CloseReasonReferenceId { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }

    // --- per-assignment stats (2026-07-23): this player's NHL + pool
    // production scoped to exactly this stint [From, To ?? today], not his
    // whole season. Refreshed nightly by ScoreCalcJob while To is null;
    // frozen at whatever they were the moment the assignment closes.

    [FirestoreProperty("gamesPlayed")]
    public int GamesPlayed { get; set; }

    [FirestoreProperty("goals")]
    public int Goals { get; set; }

    [FirestoreProperty("assists")]
    public int Assists { get; set; }

    [FirestoreProperty("plusMinus")]
    public int PlusMinus { get; set; }

    [FirestoreProperty("pim")]
    public int Pim { get; set; }

    [FirestoreProperty("shots")]
    public int Shots { get; set; }

    [FirestoreProperty("hits")]
    public int Hits { get; set; }

    [FirestoreProperty("blockedShots")]
    public int BlockedShots { get; set; }

    [FirestoreProperty("wins")]
    public int Wins { get; set; }

    [FirestoreProperty("otLosses")]
    public int OtLosses { get; set; }

    [FirestoreProperty("shutouts")]
    public int Shutouts { get; set; }

    [FirestoreProperty("goalsAgainst")]
    public int GoalsAgainst { get; set; }

    [FirestoreProperty("saves")]
    public int Saves { get; set; }

    [FirestoreProperty("shotsAgainst")]
    public int ShotsAgainst { get; set; }

    /// <summary>Fantasy points earned during this stint, per the league's RuleConfig.</summary>
    [FirestoreProperty("fantasyPoints")]
    public double FantasyPoints { get; set; }

    [FirestoreProperty("statsUpdatedUtc")]
    public Timestamp? StatsUpdatedUtc { get; set; }
}

/// <summary>Values for <see cref="Assignment.CreationEvent"/>. "draft" is reserved for the future live-draft feature (Phase 7) — not written anywhere yet.</summary>
public static class AssignmentCreationEvent
{
    public const string Draft = "draft";
    public const string Trade = "trade";
    public const string FreeAgent = "freeagent";
}

/// <summary>Values for <see cref="Assignment.CloseReason"/>. Null (unset) means the assignment is still open.</summary>
public static class AssignmentCloseReason
{
    public const string Trade = "trade";
    public const string Release = "release";
}

/// <summary>
/// One entry of a team's transaction-compensation ledger.
/// Stored at leagues/{leagueId}/teams/{username}/adjustments/{auto-id}.
/// </summary>
[FirestoreData]
public sealed class Adjustment
{
    [FirestoreProperty("delta")]
    public double Delta { get; set; }

    /// <summary>add | drop | trade.</summary>
    [FirestoreProperty("reason")]
    public string Reason { get; set; } = "";

    [FirestoreProperty("playerIdsIn")]
    public List<long> PlayerIdsIn { get; set; } = [];

    [FirestoreProperty("playerIdsOut")]
    public List<long> PlayerIdsOut { get; set; } = [];

    [FirestoreProperty("dateUtc")]
    public Timestamp DateUtc { get; set; }
}
