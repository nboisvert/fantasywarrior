using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Stats;

/// <summary>
/// One player's line in one game. Document id = "{gameId}_{playerId}".
/// Collection: `playerGameStats` (flat, queried by date for daily scoring
/// and by playerId for player pages).
/// Skater fields are null for goalies and vice versa.
/// </summary>
[FirestoreData]
public sealed class PlayerGameStats
{
    [FirestoreProperty("gameId")]
    public long GameId { get; set; }

    [FirestoreProperty("date")]
    public string Date { get; set; } = "";

    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    /// <summary>2 = regular season, 3 = playoffs.</summary>
    [FirestoreProperty("gameType")]
    public int GameType { get; set; }

    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

    [FirestoreProperty("name")]
    public string Name { get; set; } = "";

    [FirestoreProperty("teamAbbrev")]
    public string TeamAbbrev { get; set; } = "";

    [FirestoreProperty("opponentAbbrev")]
    public string OpponentAbbrev { get; set; } = "";

    [FirestoreProperty("position")]
    public string Position { get; set; } = "";

    [FirestoreProperty("isGoalie")]
    public bool IsGoalie { get; set; }

    [FirestoreProperty("toi")]
    public string Toi { get; set; } = "";

    [FirestoreProperty("pim")]
    public int Pim { get; set; }

    // --- skaters ---

    [FirestoreProperty("goals")]
    public int? Goals { get; set; }

    [FirestoreProperty("assists")]
    public int? Assists { get; set; }

    [FirestoreProperty("points")]
    public int? Points { get; set; }

    [FirestoreProperty("plusMinus")]
    public int? PlusMinus { get; set; }

    [FirestoreProperty("shots")]
    public int? Shots { get; set; }

    [FirestoreProperty("hits")]
    public int? Hits { get; set; }

    [FirestoreProperty("blockedShots")]
    public int? BlockedShots { get; set; }

    [FirestoreProperty("powerPlayGoals")]
    public int? PowerPlayGoals { get; set; }

    // --- goalies ---

    [FirestoreProperty("shotsAgainst")]
    public int? ShotsAgainst { get; set; }

    [FirestoreProperty("saves")]
    public int? Saves { get; set; }

    [FirestoreProperty("goalsAgainst")]
    public int? GoalsAgainst { get; set; }

    /// <summary>W, L or O (overtime/shootout loss); null when no decision.</summary>
    [FirestoreProperty("decision")]
    public string? Decision { get; set; }

    [FirestoreProperty("starter")]
    public bool? Starter { get; set; }

    /// <summary>Solo full game with zero goals against.</summary>
    [FirestoreProperty("shutout")]
    public bool? Shutout { get; set; }

    /// <summary>Goalie charged with an OT/SO loss (decision O).</summary>
    [FirestoreProperty("otLoss")]
    public bool? OtLoss { get; set; }

    [FirestoreProperty("syncedUtc")]
    public Timestamp SyncedUtc { get; set; }
}
