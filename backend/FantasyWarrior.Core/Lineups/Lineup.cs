using FantasyWarrior.Core.Scoring;
using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Lineups;

/// <summary>
/// One team's active roster for one scoring week. Collection:
/// leagues/{leagueId}/lineups, document id "{teamUsername}_{periodIndex}".
///
/// **One document per team per week, not one per player per week.** That is
/// ~250 documents per league-season instead of ~5,000, one read per team in
/// the nightly job instead of one per player, and — most importantly — the
/// whole active set lives in a single field, so the "max 4 defencemen" rule
/// is enforced atomically without a transaction.
///
/// **The GM and the scoring job own disjoint fields.** The GM writes only
/// <see cref="ActiveSpotIds"/>/<see cref="SubmittedUtc"/>/<see cref="SetBy"/>;
/// the job writes only <see cref="Results"/> and the rollups. Firestore does
/// not conflict two updates touching different fields of the same document, so
/// a lineup change during a scoring pass cannot be clobbered — but only as long
/// as the job never does a whole-document Set. It must always use UpdateAsync
/// with an explicit field list.
/// </summary>
[FirestoreData]
public sealed class Lineup
{
    [FirestoreProperty("teamUsername")]
    public string TeamUsername { get; set; } = "";

    [FirestoreProperty("periodIndex")]
    public int PeriodIndex { get; set; }

    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    // --- GM-owned ---

    /// <summary>
    /// The active set. One field on purpose: submitting a lineup is a single
    /// atomic write, so two tabs racing can't produce an illegal roster.
    /// </summary>
    [FirestoreProperty("activeSpotIds")]
    public List<string> ActiveSpotIds { get; set; } = [];

    [FirestoreProperty("submittedUtc")]
    public Timestamp? SubmittedUtc { get; set; }

    /// <summary>The username that set it, or "auto" when carried forward or auto-filled.</summary>
    [FirestoreProperty("setBy")]
    public string SetBy { get; set; } = LineupSetBy.Auto;

    // --- job-owned ---

    /// <summary>
    /// Per-spot scoring results for this week, keyed by roster spot id. Holds
    /// benched players too, which is what makes "you left 14 points on the
    /// bench" possible.
    /// </summary>
    [FirestoreProperty("results")]
    public Dictionary<string, LineupResult> Results { get; set; } = [];

    [FirestoreProperty("activePoints")]
    public double ActivePoints { get; set; }

    [FirestoreProperty("benchPoints")]
    public double BenchPoints { get; set; }

    [FirestoreProperty("scoredUtc")]
    public Timestamp? ScoredUtc { get; set; }

    public bool IsActive(string spotId) => ActiveSpotIds.Contains(spotId);

    public static string DocId(string teamUsername, int periodIndex) => $"{teamUsername}_{periodIndex:00}";
}

/// <summary>What one roster spot produced during one week.</summary>
[FirestoreData]
public sealed class LineupResult
{
    [FirestoreProperty("playerId")]
    public long PlayerId { get; set; }

    [FirestoreProperty("positionGroup")]
    public string PositionGroup { get; set; } = "";

    [FirestoreProperty("points")]
    public double Points { get; set; }

    [FirestoreProperty("gamesPlayed")]
    public int GamesPlayed { get; set; }

    [FirestoreProperty("stats")]
    public Dictionary<string, object>? Stats { get; set; }

    /// <summary>
    /// The days of the week this spot actually owned. Stored rather than
    /// derived so a mid-week acquisition is visible ("counted from Wednesday")
    /// and an audit needs no recomputation.
    /// </summary>
    [FirestoreProperty("fromDate")]
    public string FromDate { get; set; } = "";

    [FirestoreProperty("toDate")]
    public string ToDate { get; set; } = "";

    public StatLine Stat() => StatLine.FromMap(Stats);
}

public static class LineupSetBy
{
    public const string Auto = "auto";
}
