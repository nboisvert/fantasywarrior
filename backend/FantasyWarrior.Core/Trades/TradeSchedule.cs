using FantasyWarrior.Core.Periods;

namespace FantasyWarrior.Core.Trades;

/// <summary>
/// When an accepted trade takes effect. Pure.
///
/// <b>Always the start of the next week, never "today".</b> A swap landing
/// mid-week would give a player two owners for one scoring window, which is the
/// one thing the banked-points model assumes cannot happen — see
/// scoring-model.md §4.
///
/// This used to be nobody's decision. The nightly job executed accepted trades
/// on whichever night a week happened to bank, effective "the first week
/// starting after the last stat date" — and because banking waits a grace day,
/// that landed a week later than <c>scoring-model.md</c> §3 promised. Nothing
/// was wrong exactly, but the date was a consequence of job scheduling rather
/// than a rule, so no screen could state it and no test could pin it.
///
/// Now acceptance stamps the date and the rosters move with it, which is what
/// lets a GM set next week's lineup with the players he will actually have.
/// </summary>
public static class TradeSchedule
{
    /// <summary>
    /// The day a trade accepted on <paramref name="today"/> takes effect: the
    /// start of the first week beginning after it.
    ///
    /// Null past the last week of the season — there is no boundary left for
    /// the swap to land on, and inventing one would open roster spots into a
    /// season that has no weeks to score them.
    /// </summary>
    public static DateOnly? NextPeriodStart(IEnumerable<PeriodSpan> periods, DateOnly today)
    {
        DateOnly? best = null;
        foreach (var p in periods)
            if (p.Start > today && (best is null || p.Start < best))
                best = p.Start;
        return best;
    }
}
