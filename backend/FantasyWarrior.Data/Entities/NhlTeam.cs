namespace FantasyWarrior.Data.Entities;

/// <summary>
/// An NHL franchise, keyed by its three-letter abbreviation ("COL", "MTL").
///
/// The abbreviation is the natural key: it is what the NHL API returns on every
/// roster, boxscore and schedule payload, so keying on it means no lookup is
/// needed to write a game line. It is also what a league's "Équipe" slot stores
/// (see <see cref="Team.FranchiseAbbrev"/>) — in Les Mordus every GM owns one
/// franchise for life.
/// </summary>
public sealed class NhlTeam
{
    /// <summary>Three-letter code, e.g. "MTL".</summary>
    public required string Abbrev { get; set; }

    public required string Name { get; set; }

    public string? ConferenceName { get; set; }

    public string? DivisionName { get; set; }

    public string? LogoUrl { get; set; }
}
