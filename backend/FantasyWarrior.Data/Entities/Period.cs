namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One scoring week: Monday to Sunday on the NHL's Eastern game date. About 28
/// per season.
///
/// **Global, not per-league.** A week is a property of the NHL schedule, not of
/// a pool. Sharing the boundaries across every league is what lets the nightly
/// job fetch the week's game lines once and serve every league from the same
/// result set.
///
/// **Boundaries are immutable once written.** Points are banked per period, so
/// moving a boundary after the fact would silently restate history that teams
/// already own. The init job may only append.
///
/// There is deliberately no status column: upcoming/open/finished all derive
/// from the dates and would only drift if stored. <see cref="FinalizedUtc"/> is
/// the one piece of real job state worth persisting.
/// </summary>
public sealed class Period
{
    public int PeriodId { get; set; }

    /// <summary>e.g. "20252026".</summary>
    public required string Season { get; set; }

    /// <summary>1-based week number within the season.</summary>
    public int Number { get; set; }

    /// <summary>Inclusive ET game date. Always a Monday except possibly week 1.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive ET game date. Always a Sunday except possibly the last week.</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>
    /// When lineups freeze. Stored rather than derived so a real schedule change
    /// can be handled without a redeploy. Defaults to midnight ET on
    /// <see cref="StartDate"/>.
    /// </summary>
    public DateTime LockUtc { get; set; }

    /// <summary>
    /// Regular-season games in this window. Zero means a break week (Olympics,
    /// All-Star) so the UI can say "pause" instead of an unexplained 0 points.
    /// The 2025-26 season has two, 9–22 February 2026 for Milan-Cortina.
    /// </summary>
    public int GameCount { get; set; }

    /// <summary>
    /// Set once this period's points are banked. The single source of truth for
    /// finalization — nothing else carries an "is it done" flag.
    /// </summary>
    public DateTime? FinalizedUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public ICollection<RosterAssignment> Assignments { get; set; } = [];
}
