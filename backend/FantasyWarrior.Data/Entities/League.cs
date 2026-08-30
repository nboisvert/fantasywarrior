namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A pool. The multi-tenancy root: teams, roster spots, trades and draft picks
/// all hang off one.
///
/// <see cref="JoinCode"/> is what the API exposes as the league's <c>id</c>.
/// Under Firestore the document id doubled as the invite code and the frontend
/// keeps it in localStorage; exposing a short code rather than an integer keeps
/// both the join flow and the stored value working untouched.
///
/// <b>It carries no rules.</b> Every setting a commissioner can change — the
/// cap, roster bounds, the scale, protections, the draft — lives on a
/// <see cref="LeagueSeason"/>, one document per season. Rules have a season:
/// they used to be ten columns here plus a <c>LeagueScoringRules</c> table, all
/// mutated in place, so a keeper pool had no way to answer "what were season 2's
/// rules?". What is left here is identity and membership.
/// </summary>
public sealed class League
{
    public int LeagueId { get; set; }

    public required string Name { get; set; }

    /// <summary>NHL season the league runs on, e.g. "20252026".</summary>
    public required string Season { get; set; }

    /// <summary>Short, unambiguous invite code. Unique; also the public id.</summary>
    public required string JoinCode { get; set; }

    public int CommissionerUserId { get; set; }

    public DateTime CreatedUtc { get; set; }

    public User? Commissioner { get; set; }

    public ICollection<LeagueMember> Members { get; set; } = [];

    public ICollection<Team> Teams { get; set; } = [];

    public ICollection<RosterSpot> RosterSpots { get; set; } = [];

    public ICollection<Trade> Trades { get; set; } = [];

    public ICollection<DraftPick> DraftPicks { get; set; } = [];

    /// <summary>
    /// This league's own playthrough history — see <see cref="LeagueSeason"/>.
    /// Exactly one row here is ever not <c>Complete</c>, and **that row carries
    /// the rules**: every setting a commissioner can change lives on a season,
    /// not here.
    /// </summary>
    public ICollection<LeagueSeason> Seasons { get; set; } = [];
}
