using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Tests.Scoring;

public class StatLineTests
{
    [Fact]
    public void MissingKeysReadAsZero()
    {
        Assert.Equal(0, StatLine.Empty[StatKeys.Goals]);
        Assert.Equal(0, StatLine.Empty["a stat nobody tracks"]);
    }

    [Fact]
    public void FromGameLine_ReadsASkaterLine()
    {
        var line = new PlayerGameStats
        {
            Goals = 2, Assists = 1, PlusMinus = 3, Pim = 4, Shots = 7, Hits = 5, BlockedShots = 2,
        };

        var stats = StatLine.FromGameLine(line);

        Assert.Equal(1, stats[StatKeys.GamesPlayed]);
        Assert.Equal(2, stats[StatKeys.Goals]);
        Assert.Equal(1, stats[StatKeys.Assists]);
        Assert.Equal(3, stats[StatKeys.PlusMinus]);
        Assert.Equal(4, stats[StatKeys.Pim]);
        Assert.Equal(7, stats[StatKeys.Shots]);
        Assert.Equal(5, stats[StatKeys.Hits]);
        Assert.Equal(2, stats[StatKeys.BlockedShots]);
        // A skater has no goalie stats, and that is not an error.
        Assert.Equal(0, stats[StatKeys.Saves]);
        Assert.Equal(0, stats[StatKeys.Wins]);
    }

    [Fact]
    public void FromGameLine_ReadsAGoalieLine()
    {
        var line = new PlayerGameStats
        {
            Decision = "W", Shutout = true, Saves = 31, ShotsAgainst = 31, GoalsAgainst = 0,
        };

        var stats = StatLine.FromGameLine(line);

        Assert.Equal(1, stats[StatKeys.Wins]);
        Assert.Equal(1, stats[StatKeys.Shutouts]);
        Assert.Equal(31, stats[StatKeys.Saves]);
        Assert.Equal(0, stats[StatKeys.OtLosses]);
    }

    [Fact]
    public void FromGameLine_CountsAnOtLossButNotAWin()
    {
        var stats = StatLine.FromGameLine(new PlayerGameStats { Decision = "O", OtLoss = true, GoalsAgainst = 3 });

        Assert.Equal(0, stats[StatKeys.Wins]);
        Assert.Equal(1, stats[StatKeys.OtLosses]);
    }

    [Fact]
    public void FromGameLine_TreatsNullSkaterStatsAsZeroNotMissingGames()
    {
        // Goalie lines leave every skater field null; the game itself still counts.
        var stats = StatLine.FromGameLine(new PlayerGameStats { Decision = "L" });

        Assert.Equal(1, stats[StatKeys.GamesPlayed]);
        Assert.Equal(0, stats[StatKeys.Goals]);
    }

    [Fact]
    public void Plus_AddsEveryKeyFromBothSides()
    {
        var a = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 2, [StatKeys.Assists] = 1 });
        var b = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 3, [StatKeys.Hits] = 4 });

        var sum = a.Plus(b);

        Assert.Equal(5, sum[StatKeys.Goals]);
        Assert.Equal(1, sum[StatKeys.Assists]);
        Assert.Equal(4, sum[StatKeys.Hits]);
    }

    [Fact]
    public void Plus_DoesNotMutateEitherOperand()
    {
        var a = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 2 });
        var b = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 3 });

        a.Plus(b);

        Assert.Equal(2, a[StatKeys.Goals]);
        Assert.Equal(3, b[StatKeys.Goals]);
    }

    [Fact]
    public void Sum_OfNothingIsEmpty() => Assert.Equal(0, StatLine.Sum([])[StatKeys.Goals]);

    [Fact]
    public void ToMap_DropsZeroesSoStoredDocumentsStaySmall()
    {
        var stats = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 2, [StatKeys.Hits] = 0 });

        var map = stats.ToMap();

        Assert.Equal(2, map[StatKeys.Goals]);
        Assert.False(map.ContainsKey(StatKeys.Hits));
    }

    [Fact]
    public void FromMap_RoundTripsAndAcceptsFirestoreLongs()
    {
        // Firestore hands integers back boxed as long.
        var stats = StatLine.FromMap(new Dictionary<string, object> { [StatKeys.Goals] = 4L, [StatKeys.Assists] = 2 });

        Assert.Equal(4, stats[StatKeys.Goals]);
        Assert.Equal(2, stats[StatKeys.Assists]);
    }

    [Fact]
    public void FromMap_HandlesNullAndEmpty()
    {
        Assert.Equal(0, StatLine.FromMap(null)[StatKeys.Goals]);
        Assert.Equal(0, StatLine.FromMap(new Dictionary<string, object>())[StatKeys.Goals]);
    }

    [Fact]
    public void Score_AppliesTheLeagueScale()
    {
        var stats = new StatLine(new Dictionary<string, int>
        {
            [StatKeys.Goals] = 3, [StatKeys.Assists] = 2, [StatKeys.Wins] = 1, [StatKeys.OtLosses] = 1,
        });

        // Les Mordus: goal 1, assist 1, win 1, OT loss 1, shutout 0.
        var points = stats.Score(new Dictionary<string, double>
        {
            [StatKeys.Goals] = 1, [StatKeys.Assists] = 1,
            [StatKeys.Wins] = 1, [StatKeys.OtLosses] = 1, [StatKeys.Shutouts] = 0,
        });

        Assert.Equal(7, points);
    }

    [Fact]
    public void Score_IgnoresStatsTheLeagueDoesNotScore()
    {
        var stats = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 1, [StatKeys.Hits] = 99 });

        Assert.Equal(1, stats.Score(new Dictionary<string, double> { [StatKeys.Goals] = 1 }));
    }

    [Fact]
    public void Score_CanScoreAStatTheEngineNeverAnticipated()
    {
        // The whole point of a keyed map: a commissioner can score blocked
        // shots, or games played, without a schema change.
        var stats = new StatLine(new Dictionary<string, int>
        {
            [StatKeys.BlockedShots] = 4, [StatKeys.GamesPlayed] = 2,
        });

        var points = stats.Score(new Dictionary<string, double>
        {
            [StatKeys.BlockedShots] = 0.5, [StatKeys.GamesPlayed] = 1,
        });

        Assert.Equal(4, points);
    }

    [Fact]
    public void Score_SupportsNegativeValues()
    {
        var stats = new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 2, [StatKeys.Pim] = 10 });

        Assert.Equal(1, stats.Score(new Dictionary<string, double>
        {
            [StatKeys.Goals] = 1, [StatKeys.Pim] = -0.1,
        }));
    }
}
