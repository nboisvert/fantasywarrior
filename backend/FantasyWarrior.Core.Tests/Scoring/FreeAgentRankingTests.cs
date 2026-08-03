using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

public class FreeAgentRankingTests
{
    private static readonly Dictionary<string, double> Scale = new()
    {
        [StatKeys.Goals] = 2, [StatKeys.Assists] = 1,
        [StatKeys.Wins] = 3, [StatKeys.Saves] = 0.1,
    };

    [Fact]
    public void ScoresGoaliesAndSkatersUnderTheSameScale()
    {
        var skater = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 2, [StatKeys.Assists] = 1 });
        var goalie = new StatLine(new Dictionary<string, int> { [StatKeys.Wins] = 1, [StatKeys.Saves] = 20 });

        var ranked = FreeAgentRanking.Rank([(1L, skater), (2L, goalie)], Scale, take: 10);

        Assert.Equal(2, ranked.Count);
        // Goalie: 3 + 2 = 5. Skater: 4 + 1 = 5 -- tie, both present.
        Assert.Equal(5, ranked.Single(r => r.PlayerId == 1).Points);
        Assert.Equal(5, ranked.Single(r => r.PlayerId == 2).Points);
    }

    [Fact]
    public void DropsPlayersWhoScoredNothing()
    {
        var scored = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 1 });
        var unscored = new StatLine(new Dictionary<string, int> { [StatKeys.Pim] = 5 });

        var ranked = FreeAgentRanking.Rank([(1L, scored), (2L, unscored)], Scale, take: 10);

        Assert.Single(ranked);
        Assert.Equal(1, ranked[0].PlayerId);
    }

    [Fact]
    public void OrdersDescendingByPoints()
    {
        var low = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 1 });
        var high = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 5 });

        var ranked = FreeAgentRanking.Rank([(1L, low), (2L, high)], Scale, take: 10);

        Assert.Equal([2L, 1L], ranked.Select(r => r.PlayerId));
    }

    [Fact]
    public void TakeTruncatesToTheRequestedCount()
    {
        var lines = Enumerable.Range(1, 10)
            .Select(i => ((long)i, new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = i })));

        var ranked = FreeAgentRanking.Rank(lines, Scale, take: 4);

        Assert.Equal(4, ranked.Count);
        Assert.Equal([10L, 9L, 8L, 7L], ranked.Select(r => r.PlayerId));
    }
}
