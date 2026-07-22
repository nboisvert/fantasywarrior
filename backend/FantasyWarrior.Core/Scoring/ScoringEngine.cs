namespace FantasyWarrior.Core.Scoring;

/// <summary>Raw season counting stats for one player (league-agnostic).</summary>
public sealed record PlayerRawTotals(
    int GamesPlayed = 0,
    int Goals = 0,
    int Assists = 0,
    int Wins = 0,
    int OtLosses = 0,
    int Shutouts = 0);

/// <summary>A roster entry fed to the engine.</summary>
public sealed record RosterEntry(long PlayerId, string Position, PlayerRawTotals Totals);

public sealed record TeamScoreResult(
    double RawTopX,
    IReadOnlyList<long> CountedPlayerIds,
    IReadOnlyDictionary<long, double> PlayerPoints);

/// <summary>
/// Pure scoring rules. Team score displayed to users is
/// RawTopX + adjustmentsTotal (the adjustment ledger keeps team totals
/// invariant across transactions).
/// </summary>
public static class ScoringEngine
{
    public static double PlayerPoints(PlayerRawTotals t, PointValues v) =>
        t.Goals * v.Goal
        + t.Assists * v.Assist
        + t.Wins * v.GoalieWin
        + t.OtLosses * v.GoalieOtLoss
        + t.Shutouts * v.Shutout;

    /// <summary>
    /// Ranks each position group by fantasy points (ties: playerId asc for
    /// determinism), keeps the configured top X per group (null = all), and
    /// sums the kept points.
    /// </summary>
    public static TeamScoreResult TeamScore(IEnumerable<RosterEntry> roster, RuleConfig config)
    {
        var entries = roster
            .Select(e => (e.PlayerId, e.Position, Points: PlayerPoints(e.Totals, config.PointValues)))
            .ToList();
        return TeamScoreFromPoints(entries, config.TopCount);
    }

    /// <summary>Same selection when per-player fantasy points are already known.</summary>
    public static TeamScoreResult TeamScoreFromPoints(
        IReadOnlyCollection<(long PlayerId, string Position, double Points)> entries, TopCount topCount)
    {
        var points = entries.ToDictionary(e => e.PlayerId, e => e.Points);
        var counted = new List<long>();
        double total = 0;

        foreach (var group in entries.GroupBy(e => PositionGroups.From(e.Position)))
        {
            var ranked = group
                .OrderByDescending(e => e.Points)
                .ThenBy(e => e.PlayerId);
            var limit = topCount.For(group.Key);
            foreach (var entry in limit is null ? ranked : ranked.Take(limit.Value))
            {
                counted.Add(entry.PlayerId);
                total += entry.Points;
            }
        }

        counted.Sort();
        return new TeamScoreResult(total, counted, points);
    }

    /// <summary>
    /// Adjustment delta to append to the ledger so the team total does not
    /// move at transaction time: score = rawTopX + adjustments stays equal.
    /// </summary>
    public static double TransactionAdjustment(double rawTopXBefore, double rawTopXAfter) =>
        rawTopXBefore - rawTopXAfter;
}
