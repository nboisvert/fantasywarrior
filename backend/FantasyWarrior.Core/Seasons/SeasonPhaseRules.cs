namespace FantasyWarrior.Core.Seasons;

/// <summary>
/// The state machine one <c>LeagueSeason</c> row walks, and what each phase
/// permits elsewhere in the app. Pure — no entity, no database, no clock.
/// </summary>
public static class SeasonPhaseRules
{
    /// <summary>
    /// The one legal next step from a phase, or null from <see cref="LeagueSeasonPhase.Complete"/>
    /// — there is no next step on the same row; the season after it is a new row
    /// entirely, starting at <see cref="LeagueSeasonPhase.Preparing"/>.
    ///
    /// Deliberately linear and one step at a time, the same reasoning
    /// <c>RosterWindow</c>'s spot dates rest on: skipping a phase (going straight
    /// from <see cref="LeagueSeasonPhase.Protecting"/> to
    /// <see cref="LeagueSeasonPhase.PreSeason"/>, say) would mean a draft that
    /// never happened, silently.
    /// </summary>
    public static LeagueSeasonPhase? Next(LeagueSeasonPhase phase) => phase switch
    {
        LeagueSeasonPhase.Preparing => LeagueSeasonPhase.Protecting,
        LeagueSeasonPhase.Protecting => LeagueSeasonPhase.Drafting,
        LeagueSeasonPhase.Drafting => LeagueSeasonPhase.PreSeason,
        LeagueSeasonPhase.PreSeason => LeagueSeasonPhase.InSeason,
        LeagueSeasonPhase.InSeason => LeagueSeasonPhase.Complete,
        _ => null,
    };

    /// <summary>Is <paramref name="to"/> the one legal next step from <paramref name="from"/>?</summary>
    public static bool CanTransition(LeagueSeasonPhase from, LeagueSeasonPhase to) => Next(from) == to;

    /// <summary>
    /// May a trade be made while a league's active season sits in this phase?
    ///
    /// False only for <see cref="LeagueSeasonPhase.Protecting"/> and
    /// <see cref="LeagueSeasonPhase.Drafting"/>. The reason is a real failure
    /// mode, not caution for its own sake: a trade closes a roster spot and
    /// opens a new one, and the new one inherits no protection at all — a
    /// player a GM had just protected would silently become stealable. Freezing
    /// trades for the two phases where a protection or a steal can still happen
    /// is what keeps that from occurring.
    /// </summary>
    public static bool CanTrade(LeagueSeasonPhase phase) =>
        phase is not (LeagueSeasonPhase.Protecting or LeagueSeasonPhase.Drafting);
}
