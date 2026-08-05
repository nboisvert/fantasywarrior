namespace FantasyWarrior.Core.Scoring;

/// <summary>
/// One NHL game reduced to what a franchise's record needs: who played, who
/// scored more, and whether it ended in regulation.
///
/// A plain record rather than the <c>Game</c> entity so this whole file stays
/// pure and testable without a database — the same reason
/// <see cref="Lineups.LineupCandidate"/> is not a <c>RosterSpot</c>.
/// </summary>
/// <param name="LastPeriodType">REG, OT or SO, as the NHL API reports it.</param>
public readonly record struct GameResult(
    string HomeAbbrev,
    string AwayAbbrev,
    int HomeScore,
    int AwayScore,
    string? LastPeriodType);

/// <summary>
/// A franchise's own record over a set of games — what the Équipe roster slot
/// scores.
///
/// The slot is an ordinary <see cref="Lineups.LineupCandidate"/> everywhere
/// else in the system: it opens a roster spot, it produces one
/// <c>RosterAssignment</c> per week, its points bank like anyone's. The only
/// thing it does not share with a player is where its stats come from — the
/// <c>Games</c> table rather than the game log — and this is that difference,
/// isolated in one pure function.
/// </summary>
public static class FranchiseResults
{
    /// <summary>Overtime and the shootout both end a game past regulation.</summary>
    private static bool PastRegulation(string? lastPeriodType) =>
        lastPeriodType is "OT" or "SO";

    /// <summary>
    /// Whether the game is over. The NHL only publishes an outcome once it is,
    /// so an absent <see cref="GameResult.LastPeriodType"/> means scheduled,
    /// in progress or postponed — **not** a 0-0 regulation loss for both sides,
    /// which is what reading the score alone would conclude.
    /// </summary>
    private static bool IsFinal(GameResult game) => !string.IsNullOrEmpty(game.LastPeriodType);

    /// <summary>
    /// Wins, regulation losses and overtime losses for one franchise.
    ///
    /// Deliberately no <c>gamesPlayed</c>: the Équipe slot reports a record,
    /// not a workload (Nick, 2026-08-05), and leaving it out is also what keeps
    /// <c>vStandings.RosterGamesPlayed</c> — the denominator behind every
    /// points-per-game figure in the app — about players only.
    ///
    /// A game the franchise did not play in contributes nothing, so the caller
    /// can hand over a whole week's schedule rather than pre-filtering it once
    /// per team.
    /// </summary>
    public static StatLine For(string abbrev, IEnumerable<GameResult> games)
    {
        int wins = 0, losses = 0, otLosses = 0;

        foreach (var game in games)
        {
            if (!IsFinal(game)) continue;

            var isHome = game.HomeAbbrev == abbrev;
            var isAway = game.AwayAbbrev == abbrev;
            if (!isHome && !isAway) continue;

            var scored = isHome ? game.HomeScore : game.AwayScore;
            var allowed = isHome ? game.AwayScore : game.HomeScore;

            if (scored > allowed) wins++;
            else if (PastRegulation(game.LastPeriodType)) otLosses++;
            else losses++;
        }

        return new StatLine(new Dictionary<string, int>
        {
            [StatKeys.TeamWins] = wins,
            [StatKeys.TeamLosses] = losses,
            [StatKeys.TeamOtLosses] = otLosses,
        });
    }
}
