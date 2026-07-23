using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Stats;

/// <summary>
/// Consolidated per-season totals for one player. Doc id "{season}_{playerId}"
/// in collection `playerSeasonStats` — a write-through cache over
/// `playerGameStats`, not a separate source of truth: it's always derived
/// from (and can be fully rebuilt from) the per-game lines.
///
/// Populated as a side effect of nightly scoring (one doc per rostered
/// player, refreshed every run) and of bulk season-wide operations like
/// drafting (which already scan every game line and can persist the result
/// for everyone in one pass). See <see cref="Scoring.PlayerTotalsSource"/>.
/// </summary>
[FirestoreData]
public sealed class PlayerSeasonStats
{
    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

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

    [FirestoreProperty("updatedUtc")]
    public Timestamp UpdatedUtc { get; set; }
}
