using FantasyWarrior.Core.Seasons;

namespace FantasyWarrior.Data.Entities;

/// <summary>
/// One league's playthrough of one NHL season — protect, draft, play, close.
///
/// This is the table half of "season" (see <c>season-lifecycle.md</c> §1/§4).
/// <c>League.Season</c> stays a plain value naming the NHL season whose points
/// currently count (<see cref="Core.Seasons.Season"/>); this row is where a
/// keeper league gets to be at "season 4" the way its own commissioner counts
/// it, where <see cref="Phase"/> lives, and where a champion is written once
/// the season closes.
///
/// <c>League.Season</c> matches a row here by value, not by a stored foreign
/// key — a composite FK was the first thing tried and does not work: a brand
/// new league's row has to exist before any <see cref="LeagueSeason"/> row can
/// reference its <see cref="LeagueId"/>, so a constraint requiring the reverse
/// would refuse the very insert that has to come first.
///
/// **Exactly one row per league is ever not <see cref="LeagueSeasonPhase.Complete"/>.**
/// The off-season phases belong to the season being *prepared*, which is why
/// this can be true even while <see cref="League.Season"/> still names the one
/// that just finished — the standings stay on last year's numbers straight
/// through the protection window and the draft, and flip only when this row
/// reaches <see cref="LeagueSeasonPhase.InSeason"/>.
/// </summary>
public sealed class LeagueSeason
{
    public int LeagueSeasonId { get; set; }

    public int LeagueId { get; set; }

    /// <summary>The NHL season this row plays, e.g. "20262027". See <see cref="Core.Seasons.Season"/>.</summary>
    public required string Season { get; set; }

    /// <summary>
    /// The league's own count — "saison 3", "saison 4" — the way Les Mordus has
    /// always numbered its own history, independent of which NHL season it maps
    /// to. Not derivable from <see cref="Season"/>: a league can join or skip a
    /// year the NHL does not.
    /// </summary>
    public int Number { get; set; }

    public LeagueSeasonPhase Phase { get; set; }

    /// <summary>Written when this row completes — the top of that season's own standings, not today's.</summary>
    public int? ChampionTeamId { get; set; }

    public DateTime StartedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public League? League { get; set; }

    public Team? ChampionTeam { get; set; }
}
