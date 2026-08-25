namespace FantasyWarrior.Data.Entities;

/// <summary>Why a roster spot opened.</summary>
public enum RosterSpotStartReason : byte
{
    FreeAgent = 0,
    Draft = 1,
    Trade = 2,
}

/// <summary>Why a roster spot closed.</summary>
public enum RosterSpotEndReason : byte
{
    Release = 0,
    Trade = 1,

    /// <summary>Lost in the off-season steal draft. See <c>scoring-model.md</c> §11.</summary>
    Draft = 2,
}

/// <summary>
/// Whether the GM has spent one of his protection slots on this spot, keeping
/// its player out of reach in the coming off-season draft.
///
/// **The GM's decision, and nothing else.** "Auto-protected" — too few NHL games
/// to be draftable at all — is deliberately absent: that is a fact about the
/// player, derived from <see cref="Player.CareerNhlGames"/> by
/// <c>ProtectionRules.IsAutoProtected</c>, and it applies to a free agent who
/// has no spot at all. Folding it in here would also destroy the distinction the
/// protection phase needs most: "untouchable anyway" and "the GM burned a slot on
/// him" are not the same answer.
///
/// **Ephemeral** (Nick, 2026-08-25). It only means anything between two seasons
/// and resets to <see cref="Unprotected"/> for everyone at the start of each one
/// — which is why it is a column rather than a row per draft. There is no
/// history to keep.
/// </summary>
public enum RosterProtectionStatus : byte
{
    Unprotected = 0,
    Protected = 1,
}

/// <summary>
/// One stint of an asset belonging to a team: from the day it was acquired to
/// the day it left, or open-ended if it is still held.
///
/// **Spots are never deleted, only closed.** A team keeps the points a player
/// banked for it even after he is traded away, so a closed spot stays part of
/// that team's history forever. This is what makes trades free of retroactive
/// effects — see scoring-model.md §4.
///
/// The start and end references are real foreign keys rather than the
/// stringly-typed <c>refId</c> the Firestore model carried, so "which trade
/// brought him here" is a join, and a dangling reference is impossible.
///
/// <b>The asset is a player or an NHL franchise</b> — the Équipe slot, which
/// scores its own team's record. That polymorphism was rejected on 2026-08-05
/// on the grounds that it "would contaminate the whole roster, lineup and
/// transaction model"; Nick reversed it the same day, and the reversal is what
/// the shape here is worth. A franchise behaves like a player in every way the
/// system cares about: it opens a spot, it produces one
/// <see cref="RosterAssignment"/> per week, its points bank, it can be traded.
/// Giving it a parallel model would have meant a second scoring engine, a
/// second trade path and a second grid; giving it a nullable column and a CHECK
/// constraint costs what you see. The one thing it does not share with a player
/// is where its stats come from — <c>Games</c> rather than the game log — and
/// that lives in <c>FranchiseResults</c>.
/// </summary>
public sealed class RosterSpot
{
    public int RosterSpotId { get; set; }

    /// <summary>
    /// Denormalised from <see cref="Team"/> so league-wide uniqueness can be a
    /// filtered unique index on (LeagueId, PlayerId) — a team-scoped index
    /// could not express "one owner per player per league", which under
    /// Firestore was an application-level scan of every team in the league.
    /// </summary>
    public int LeagueId { get; set; }

    public int TeamId { get; set; }

    /// <summary>
    /// Null on an Équipe spot, which holds <see cref="FranchiseAbbrev"/>
    /// instead. Exactly one of the two is set, and it agrees with
    /// <see cref="PositionGroup"/> — a CHECK constraint, not a convention.
    /// </summary>
    public long? PlayerId { get; set; }

    /// <summary>
    /// The NHL franchise this spot holds, on an Équipe spot only. Null for a
    /// player.
    ///
    /// Distinct from <see cref="Entities.Team.FranchiseAbbrev"/>, which is the
    /// pool team's own identity and never moves (Nick, 2026-08-05). These two
    /// start out equal in Les Mordus and are meant to be able to diverge: the
    /// club you are is not the club you own.
    /// </summary>
    public string? FranchiseAbbrev { get; set; }

    /// <summary>
    /// F, D, G — or T for the Équipe slot — frozen when the spot opens.
    /// Intentionally a copy rather than a lookup: a player's slot eligibility
    /// stays stable for the life of the spot even if the NHL relists his
    /// position mid-season.
    /// </summary>
    public required string PositionGroup { get; set; }

    /// <summary>The Équipe slot: this spot holds a franchise, not a player.</summary>
    public bool IsFranchise => PositionGroup == "T";

    /// <summary>Inclusive ET date the stint began.</summary>
    public DateOnly StartDate { get; set; }

    public RosterSpotStartReason StartReason { get; set; }

    /// <summary>Set when <see cref="StartReason"/> is Trade.</summary>
    public int? StartTradeId { get; set; }

    /// <summary>Set when <see cref="StartReason"/> is Draft.</summary>
    public int? StartDraftPickId { get; set; }

    /// <summary>Inclusive ET date the stint ended; null means still held.</summary>
    public DateOnly? EndDate { get; set; }

    public RosterSpotEndReason? EndReason { get; set; }

    public int? EndTradeId { get; set; }

    /// <summary>
    /// Whether the GM has protected this spot for the coming off-season draft.
    /// Resets to <see cref="RosterProtectionStatus.Unprotected"/> for the whole
    /// league each season — see the enum.
    /// </summary>
    public RosterProtectionStatus ProtectionStatus { get; set; }

    public DateTime OpenedUtc { get; set; }

    public DateTime? ClosedUtc { get; set; }

    public League? League { get; set; }

    public Team? Team { get; set; }

    public Player? Player { get; set; }

    public NhlTeam? Franchise { get; set; }

    public Trade? StartTrade { get; set; }

    public Trade? EndTrade { get; set; }

    public DraftPick? StartDraftPick { get; set; }

    /// <summary>One row per scoring week this spot existed for.</summary>
    public ICollection<RosterAssignment> Assignments { get; set; } = [];

    public bool IsOpen => EndDate is null;
}
