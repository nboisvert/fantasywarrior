using Google.Cloud.Firestore;

namespace FantasyWarrior.Core.Periods;

/// <summary>
/// One scoring week. Collection: `periods`, document id "{season}-W{index:00}".
///
/// **Global, not per-league.** A week is a property of the NHL schedule, not of
/// a pool, and sharing boundaries across every league is what lets the nightly
/// job fetch the week's game lines in a single date-range query instead of one
/// query per league. Per-league calendars would put that cost back.
///
/// **Boundaries are immutable once written.** Points are banked per period, so
/// moving a boundary after the fact would silently restate history. The init
/// job may only append.
///
/// Note there is no `status` field: upcoming/open/finished are all derivable
/// from the dates and would only drift if stored. <see cref="FinalizedUtc"/> is
/// the one piece of real job state worth persisting.
/// </summary>
[FirestoreData]
public sealed class Period
{
    [FirestoreProperty("season")]
    public string Season { get; set; } = "";

    /// <summary>1-based week number within the season.</summary>
    [FirestoreProperty("index")]
    public int Index { get; set; }

    /// <summary>Inclusive ET game date, always a Monday except possibly the first week.</summary>
    [FirestoreProperty("startDate")]
    public string StartDate { get; set; } = "";

    /// <summary>Inclusive ET game date, always a Sunday except possibly the last week.</summary>
    [FirestoreProperty("endDate")]
    public string EndDate { get; set; } = "";

    /// <summary>
    /// When lineups freeze. Stored rather than derived so it can be tuned for a
    /// season without a redeploy. Defaults to midnight ET on <see cref="StartDate"/>.
    /// </summary>
    [FirestoreProperty("lockUtc")]
    public Timestamp LockUtc { get; set; }

    /// <summary>
    /// NHL regular-season games in this window. Zero means a break week (All-Star,
    /// bye) — the UI can say so instead of showing an ambiguous 0 points.
    /// </summary>
    [FirestoreProperty("gameCount")]
    public int GameCount { get; set; }

    /// <summary>
    /// Set once the period's points are banked. The single source of truth for
    /// finalization; nothing else should carry a "is it done" flag.
    /// </summary>
    [FirestoreProperty("finalizedUtc")]
    public Timestamp? FinalizedUtc { get; set; }

    [FirestoreProperty("createdUtc")]
    public Timestamp CreatedUtc { get; set; }
}

public static class PeriodId
{
    public static string For(string season, int index) => $"{season}-W{index:00}";
}
