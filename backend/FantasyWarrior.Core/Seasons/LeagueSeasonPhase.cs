namespace FantasyWarrior.Core.Seasons;

/// <summary>
/// Where one league's season sits in its own lifecycle — separate from
/// <c>League.Season</c>, which only answers "whose points count right now".
///
/// **Lives on the season, not the league.** "The league is drafting" cannot say
/// *for which season* — a keeper league spends its off-season protecting and
/// drafting players for the season it is about to start while its standings
/// still show the one that just finished. See <c>season-lifecycle.md</c> §5.
///
/// **Exactly one <c>LeagueSeason</c> row per league is ever anything but
/// <see cref="Complete"/>.** That is enforced as a database constraint
/// (a filtered unique index), not merely a convention here.
///
/// Only half of what this models is derivable from dates the way
/// <see cref="Periods.PeriodCalendar"/>'s spans are — "is the season being
/// played" is a date lookup, but "has the protection window closed, is the
/// draft running" is a decision a commissioner makes, and no calendar knows it.
/// That is why this is a stored column where <c>Period</c> deliberately has
/// none.
/// </summary>
public enum LeagueSeasonPhase : byte
{
    /// <summary>Nothing is open yet. The starting state for a season not yet playing.</summary>
    Preparing = 0,

    /// <summary>Each GM chooses who to protect. Trades are frozen — see <see cref="SeasonPhaseRules"/>.</summary>
    Protecting = 1,

    /// <summary>The steal rounds. Trades stay frozen.</summary>
    Drafting = 2,

    /// <summary>
    /// Trades reopen. A team that came out of the draft under the roster
    /// minimum gets this window to fix it before lineups matter again.
    /// </summary>
    PreSeason = 3,

    /// <summary>The season is being played. <c>League.Season</c> points here.</summary>
    InSeason = 4,

    /// <summary>Banked and closed for good. Its champion is written and nothing about it changes again.</summary>
    Complete = 5,
}
