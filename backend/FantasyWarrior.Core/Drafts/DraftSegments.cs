namespace FantasyWarrior.Core.Drafts;

/// <summary>
/// Which of the two drafts a turn belongs to. One off-season, two drafts, run
/// back to back inside the single <c>Drafting</c> phase.
///
/// They differ in every way that matters, which is exactly why the segment is
/// a stored value rather than something inferred at the point of use:
/// <list type="bullet">
///   <item>where the turn comes from — <see cref="Steal"/> turns are derived
///   and cannot be traded, <see cref="Rookie"/> turns are rows in
///   <c>DraftPicks</c> and change hands;</item>
///   <item>who is available — a rival's exposed player, versus nobody's
///   player at all;</item>
///   <item>what the selection consumes — nothing, versus one entitlement.</item>
/// </list>
/// </summary>
public enum DraftSegment : byte
{
    /// <summary>The steal rounds. A GM takes an exposed player off a rival's roster.</summary>
    Steal = 0,

    /// <summary>The rookie / free-agent rounds, spending the tradable <c>DraftPick</c> rows.</summary>
    Rookie = 1,
}

/// <summary>
/// One turn in the draft: who picks, when, and — in the rookie segment — which
/// entitlement it spends.
/// </summary>
/// <param name="OverallIndex">
/// 0-based and continuous <b>across both segments</b>. This is the turn's
/// identity: it is what the unique index on <c>DraftSelections</c> guards, and
/// therefore what makes two GMs racing for the same turn a database error
/// rather than a silent double-pick.
/// </param>
/// <param name="Round">1-based, within its own segment.</param>
/// <param name="DraftPickId">
/// The entitlement being spent. Null for every <see cref="DraftSegment.Steal"/>
/// turn — steal turns are not tradable, so there is deliberately no row to
/// point at.
/// </param>
public sealed record DraftTurn(
    DraftSegment Segment,
    int OverallIndex,
    int Round,
    int PickInRound,
    int TeamId,
    int? DraftPickId);

/// <summary>
/// The arithmetic that turns an overall index into a segment, a round and a
/// position in that round. Pure, and separated from <see cref="DraftOrder"/>
/// because it is the part that has nothing to do with <i>who</i> anybody is.
/// </summary>
public static class DraftSegments
{
    /// <summary>
    /// How many turns the steal segment occupies before the rookie segment
    /// starts — every team gets the same number, because a steal turn cannot be
    /// traded away or acquired.
    /// </summary>
    public static int StealTurnCount(int teamCount, int stealRounds) =>
        teamCount <= 0 || stealRounds <= 0 ? 0 : teamCount * stealRounds;

    /// <summary>Which draft an overall index falls in.</summary>
    public static DraftSegment SegmentOf(int overallIndex, int stealTurnCount) =>
        overallIndex < stealTurnCount ? DraftSegment.Steal : DraftSegment.Rookie;

    /// <summary>
    /// The 1-based steal round an overall index sits in.
    ///
    /// <b>Linear, not a snake.</b> Round 2 repeats round 1's order rather than
    /// reversing it — the pool's rule (Nick, 2026-08-25). A snake would be the
    /// same function with a conditional reversal here, and nowhere else; this
    /// is the one place that decision lives.
    /// </summary>
    public static int StealRound(int overallIndex, int teamCount) =>
        teamCount <= 0 ? 0 : overallIndex / teamCount + 1;

    /// <summary>The 1-based position within the steal round.</summary>
    public static int StealPickInRound(int overallIndex, int teamCount) =>
        teamCount <= 0 ? 0 : overallIndex % teamCount + 1;
}
