using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Stats;

/// <summary>
/// One NHL game. Document id = NHL game id. Collection: `games`.
/// </summary>
[FirestoreData]
public sealed class Game
{
    [FirestoreProperty("nhlGameId")]
    public long NhlGameId { get; set; }

    /// <summary>Season like "20252026".</summary>
    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    /// <summary>2 = regular season, 3 = playoffs.</summary>
    [FirestoreProperty("gameType")]
    public int GameType { get; set; }

    /// <summary>Game date (ET) as "YYYY-MM-DD" — the key the daily scoring runs on.</summary>
    [FirestoreProperty("date")]
    public string Date { get; set; } = "";

    [FirestoreProperty("homeAbbrev")]
    public string HomeAbbrev { get; set; } = "";

    [FirestoreProperty("awayAbbrev")]
    public string AwayAbbrev { get; set; } = "";

    [FirestoreProperty("homeScore")]
    public int HomeScore { get; set; }

    [FirestoreProperty("awayScore")]
    public int AwayScore { get; set; }

    /// <summary>REG, OT or SO.</summary>
    [FirestoreProperty("lastPeriodType")]
    public string LastPeriodType { get; set; } = "";

    [FirestoreProperty("syncedUtc")]
    public Timestamp SyncedUtc { get; set; }
}
