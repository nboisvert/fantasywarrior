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
}

/// <summary>
/// One stint of a player belonging to a team: from the day he was acquired to
/// the day he left, or open-ended if he is still held.
///
/// **Spots are never deleted, only closed.** A team keeps the points a player
/// banked for it even after he is traded away, so a closed spot stays part of
/// that team's history forever. This is what makes trades free of retroactive
/// effects — see scoring-model.md §4.
///
/// The start and end references are real foreign keys rather than the
/// stringly-typed <c>refId</c> the Firestore model carried, so "which trade
/// brought him here" is a join, and a dangling reference is impossible.
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

    public long PlayerId { get; set; }

    /// <summary>
    /// F, D or G, frozen when the spot opens. Intentionally a copy rather than
    /// a lookup: a player's slot eligibility stays stable for the life of the
    /// spot even if the NHL relists his position mid-season.
    /// </summary>
    public required string PositionGroup { get; set; }

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

    public DateTime OpenedUtc { get; set; }

    public DateTime? ClosedUtc { get; set; }

    public League? League { get; set; }

    public Team? Team { get; set; }

    public Player? Player { get; set; }

    public Trade? StartTrade { get; set; }

    public Trade? EndTrade { get; set; }

    public DraftPick? StartDraftPick { get; set; }

    /// <summary>One row per scoring week this spot existed for.</summary>
    public ICollection<RosterAssignment> Assignments { get; set; } = [];

    public bool IsOpen => EndDate is null;
}
