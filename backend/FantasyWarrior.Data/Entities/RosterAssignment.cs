namespace FantasyWarrior.Data.Entities;

/// <summary>
/// What one roster spot did in one scoring week, and whether the GM had him
/// active for it. One row per (spot, period) — about 9,800 per league-season.
///
/// **This is the one honest grain in the whole model.** Everything above it —
/// what a player has produced for this team all season, what the team has
/// scored this week, the standings — is a SUM over these rows, served by views.
/// Storing those totals as columns too is what the Firestore model did, and
/// keeping three copies of one number agreeing by hand is where its bugs came
/// from.
///
/// It replaces the Firestore <c>Lineup</c> document (one per team per week,
/// holding a map of results). That shape existed to keep the document count
/// down; here a row per spot-week costs nothing and is actually queryable —
/// "every week Crosby sat on the bench" is a WHERE clause rather than a scan of
/// parsed maps.
/// </summary>
public sealed class RosterAssignment
{
    public int RosterAssignmentId { get; set; }

    public int RosterSpotId { get; set; }

    public int PeriodId { get; set; }

    /// <summary>Whether this player counted toward the team's score this week.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The days of the period this spot actually owned — the result of
    /// <c>StatWindow.Intersect</c>, stored rather than re-derived.
    ///
    /// Usually the whole week, because transactions take effect at a period
    /// rollover by design. Not always: a free-agent add lands on the day it is
    /// made, so "counted from Wednesday" is a real state the UI shows and an
    /// audit needs without recomputation.
    /// </summary>
    public DateOnly EffectiveFrom { get; set; }

    public DateOnly EffectiveTo { get; set; }

    // --- what he produced in that window, one column per StatKeys name ---

    public int GamesPlayed { get; set; }

    public int Goals { get; set; }

    public int Assists { get; set; }

    public int PlusMinus { get; set; }

    public int Pim { get; set; }

    public int Shots { get; set; }

    public int Hits { get; set; }

    public int BlockedShots { get; set; }

    public int Wins { get; set; }

    public int OtLosses { get; set; }

    public int Shutouts { get; set; }

    public int GoalsAgainst { get; set; }

    public int Saves { get; set; }

    public int ShotsAgainst { get; set; }

    /// <summary>
    /// The stats above scored under the league's scale, at the time this row was
    /// last computed.
    ///
    /// Frozen once <see cref="IsFinalized"/> is set. That freezing is the
    /// banking rule: a week's points belong permanently to whoever fielded the
    /// player, so a later trade cannot move them and a mid-season change to the
    /// scale does not restate the past. The `recompute` command is the
    /// deliberate escape hatch.
    /// </summary>
    public double FantasyPoints { get; set; }

    /// <summary>
    /// Set when the period was banked. A finalized row is never recomputed,
    /// which is what makes the nightly job safe to re-run: the current week is
    /// always rebuilt from scratch, finished weeks are never touched again.
    /// </summary>
    public bool IsFinalized { get; set; }

    public DateTime ScoredUtc { get; set; }

    public RosterSpot? RosterSpot { get; set; }

    public Period? Period { get; set; }
}
