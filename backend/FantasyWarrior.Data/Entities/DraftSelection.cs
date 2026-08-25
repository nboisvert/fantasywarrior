using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One selection made in one draft: the event, not the ownership it produces.
///
/// <b>Why this exists rather than being derived from <see cref="RosterSpot"/>.</b>
/// The tempting shortcut is to read the draft back out of the spots it opened —
/// they already carry <c>StartReason = Draft</c>. It does not work, and the
/// reason is concrete: <c>SeedMordusJob</c> opened all 418 of Les Mordus' spots
/// with exactly that reason when the league was imported. A derivation would
/// count 418 phantom selections before the first real pick was made.
///
/// Three more reasons it would still be wrong even in a clean league: a steal is
/// a <i>pair</i> of spots with no key for the event between them; the order of a
/// draft would end up resting on an identity column's ordering; and the steal
/// segment has no entitlement row to claim, so nothing else could stop two GMs
/// from taking the same turn.
///
/// That last one is the load-bearing reason. <see cref="OverallIndex"/> with a
/// unique index is the only thing standing between an asynchronous draft and a
/// double pick.
///
/// The same distinction already exists in this schema: <c>Trade</c> and
/// <c>TradeAsset</c> live alongside <c>RosterSpot.StartTradeId</c>, and nobody
/// reconstructs trades from spot pairs. A spot records <b>ownership over
/// time</b>; this records <b>an event in a sequence</b>.
/// </summary>
public sealed class DraftSelection
{
    public int DraftSelectionId { get; set; }

    /// <summary>
    /// The draft belongs to one league season — not to the league forever. A
    /// keeper pool drafts every summer, and last summer's picks must not be
    /// counted in this summer's turn arithmetic.
    /// </summary>
    public int LeagueSeasonId { get; set; }

    /// <summary>
    /// 0-based, continuous across <b>both</b> segments. The turn's identity, and
    /// the column the unique index guards — see the class remarks.
    /// </summary>
    public int OverallIndex { get; set; }

    public DraftSegment Segment { get; set; }

    /// <summary>
    /// 1-based within its own segment. <b>Stored, not derived</b>: the steal
    /// segment's round is arithmetic on <see cref="OverallIndex"/>, but the
    /// rookie segment's comes from a tradable entitlement, so there is no single
    /// expression that yields both. One stored column makes the history feed a
    /// single query instead of two.
    /// </summary>
    public int Round { get; set; }

    /// <summary>Who selected.</summary>
    public int TeamId { get; set; }

    /// <summary>
    /// Who was taken — or <b>null when the turn was passed</b>.
    ///
    /// Passing is not a courtesy, it is a necessity: 14 teams times 2 losses is
    /// exactly the 28 turns of the steal segment, so a GM late in the order can
    /// genuinely face an empty pool. Without a way to record a turn that took
    /// nobody, the draft would deadlock on him.
    /// </summary>
    public long? PlayerId { get; set; }

    /// <summary>
    /// The team he was taken from. Set on a steal, null everywhere else — the
    /// rookie segment takes from nobody.
    /// </summary>
    public int? StolenFromTeamId { get; set; }

    /// <summary>
    /// The entitlement this selection spent. Null for every steal: steal turns
    /// are not tradable, so they deliberately have no row to point at.
    /// </summary>
    public int? DraftPickId { get; set; }

    public DateTime MadeUtc { get; set; }

    public LeagueSeason? LeagueSeason { get; set; }
    public Team? Team { get; set; }
    public Player? Player { get; set; }
    public Team? StolenFromTeam { get; set; }
    public DraftPick? DraftPick { get; set; }

    /// <summary>A turn nobody could be taken on.</summary>
    public bool IsPass => PlayerId is null;
}
