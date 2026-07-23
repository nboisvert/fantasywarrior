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

    /// <summary>How the player was acquired: initial | free_agency | trade | draft.</summary>
    [FirestoreProperty("source")]
    public string Source { get; set; } = "";

    /// <summary>Id of the source entity (trade id, draft pick id) when applicable.</summary>
    [FirestoreProperty("sourceRefId")]
    public string? SourceRefId { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }
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
