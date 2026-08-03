namespace FantasyWarrior.Core.Scoring;

/// <summary>One unrostered player's fantasy score for a period, under a league's own scale.</summary>
public sealed record FreeAgentScore(long PlayerId, StatLine Line, double Points);

/// <summary>
/// Turns a pile of per-player stat lines into a ranked free-agent leaderboard.
///
/// Scoring through the league's own scale (rather than raw NHL points) is the
/// whole reason this exists: it is what lets a goalie's wins/saves compete
/// against a skater's goals/assists on the same list.
/// </summary>
public static class FreeAgentRanking
{
    public static IReadOnlyList<FreeAgentScore> Rank(
        IEnumerable<(long PlayerId, StatLine Line)> perPlayerLines,
        IReadOnlyDictionary<string, double> scale,
        int take) =>
        perPlayerLines
            .Select(p => new FreeAgentScore(p.PlayerId, p.Line, p.Line.Score(scale)))
            .Where(s => s.Points > 0)
            .OrderByDescending(s => s.Points)
            .Take(take)
            .ToList();
}
