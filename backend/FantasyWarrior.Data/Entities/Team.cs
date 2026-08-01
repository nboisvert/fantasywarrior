namespace FantasyWarrior.Data.Entities;

/// <summary>
/// A GM's team inside a league. One per user per league.
///
/// **Deliberately carries no scores.** Twelve columns existed on the Firestore
/// version — playerIds, playerPoints, playerNhlPoints, capTotal,
/// rosterGamesPlayed, score, finalizedScore, periodPoints, benchScore,
/// currentPeriodIndex, periodScores, finalizedThroughPeriodIndex — every one of
/// them a denormalisation that existed because Firestore cannot join or
/// aggregate. Keeping them here would mean maintaining the
/// <c>score = finalizedScore + periodPoints</c> invariant by hand, which is
/// exactly the class of bug this project has already hit.
///
/// The one honest grain is <see cref="RosterAssignment.FantasyPoints"/> — what
/// a player produced for a team in a week. Everything above it is a SUM, served
/// by views. See <c>vStandings</c> and <c>vTeamPeriodScores</c>.
/// </summary>
public sealed class Team
{
    public int TeamId { get; set; }

    public int LeagueId { get; set; }

    public int OwnerUserId { get; set; }

    public required string Name { get; set; }

    /// <summary>
    /// The NHL franchise this team carries as its identity, when the league uses
    /// them. In Les Mordus every GM owns one for life; it is never traded and
    /// costs no cap.
    ///
    /// A column rather than a roster spot on purpose: a spot that could hold
    /// either a player or a whole franchise would make every roster, lineup and
    /// trade path polymorphic for one permanent, untradeable case.
    /// </summary>
    public string? FranchiseAbbrev { get; set; }

    public DateTime CreatedUtc { get; set; }

    public League? League { get; set; }

    public User? Owner { get; set; }

    public NhlTeam? Franchise { get; set; }

    public ICollection<RosterSpot> RosterSpots { get; set; } = [];

    public ICollection<TeamPeriodLineup> Lineups { get; set; } = [];
}
