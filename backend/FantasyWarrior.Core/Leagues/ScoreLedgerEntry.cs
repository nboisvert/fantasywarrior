using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Leagues;

/// <summary>
/// Audit trail entry: what changed for one player, on one team, on one day.
/// Stored at leagues/{leagueId}/teams/{username}/scoreLedger/{auto-id}.
///
/// Written only when the player's counted-status flipped or his cumulative
/// fantasy points changed (i.e., he played) — not one row per rostered
/// player every night. Purely explanatory: `Team.Score` is always the fresh
/// top-X recompute (see ScoreCalcJob) and is never derived by summing these
/// entries.
/// </summary>
[FirestoreData]
public sealed class ScoreLedgerEntry
{
    /// <summary>Day this entry covers, ET, "YYYY-MM-DD".</summary>
    [FirestoreProperty("date")]
    public string Date { get; set; } = "";

    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

    /// <summary>Denormalized so the ledger reads without joining against `players`.</summary>
    [FirestoreProperty("playerName")]
    public string PlayerName { get; set; } = "";

    [FirestoreProperty("position")]
    public string Position { get; set; } = "";

    /// <summary>Cumulative season fantasy points as of the previous run.</summary>
    [FirestoreProperty("fantasyPointsBefore")]
    public double FantasyPointsBefore { get; set; }

    /// <summary>Cumulative season fantasy points as of this run.</summary>
    [FirestoreProperty("fantasyPointsAfter")]
    public double FantasyPointsAfter { get; set; }

    [FirestoreProperty("countedBefore")]
    public bool CountedBefore { get; set; }

    [FirestoreProperty("countedAfter")]
    public bool CountedAfter { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }
}

/// <summary>Pure decision logic, pulled out of ScoreCalcJob so it's unit-testable without Firestore.</summary>
public static class ScoreLedgerLogic
{
    /// <summary>A ledger entry is worth recording only when something actually changed for this player.</summary>
    public static bool ShouldRecord(double fantasyPointsBefore, double fantasyPointsAfter, bool countedBefore, bool countedAfter) =>
        fantasyPointsBefore != fantasyPointsAfter || countedBefore != countedAfter;
}
