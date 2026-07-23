using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Tests.Scoring;

public class PlayerTotalsSourceTests
{
    private static PlayerGameStats Line(string date, int goals, int assists) => new()
    {
        Date = date,
        Season = "20252026",
        GameType = 2,
        Goals = goals,
        Assists = assists,
    };

    [Fact]
    public void AggregateRange_KeepsOnlyLinesWithinInclusiveBounds()
    {
        var lines = new[]
        {
            Line("2026-01-01", goals: 1, assists: 0), // before range
            Line("2026-01-10", goals: 2, assists: 1), // in range (boundary)
            Line("2026-01-15", goals: 1, assists: 1), // in range
            Line("2026-01-20", goals: 5, assists: 5), // in range (boundary)
            Line("2026-01-25", goals: 9, assists: 9), // after range
        };

        var totals = PlayerTotalsSource.AggregateRange(lines, "20252026", "2026-01-10", "2026-01-20");

        Assert.Equal(3, totals.GamesPlayed);
        Assert.Equal(8, totals.Goals); // 2+1+5
        Assert.Equal(7, totals.Assists); // 1+1+5
    }

    [Fact]
    public void AggregateRange_NullToMeansStillOpen_IncludesEverythingFromStart()
    {
        var lines = new[]
        {
            Line("2026-01-05", goals: 1, assists: 0),
            Line("2026-03-01", goals: 2, assists: 2),
        };

        var totals = PlayerTotalsSource.AggregateRange(lines, "20252026", "2026-01-01", to: null);

        Assert.Equal(2, totals.GamesPlayed);
        Assert.Equal(3, totals.Goals);
    }

    [Fact]
    public void AggregateRange_IgnoresOtherSeasonsAndGameTypes()
    {
        var lines = new[]
        {
            Line("2026-01-10", goals: 1, assists: 1),
            new PlayerGameStats { Date = "2026-01-11", Season = "20242025", GameType = 2, Goals = 9, Assists = 9 },
            new PlayerGameStats { Date = "2026-01-12", Season = "20252026", GameType = 3, Goals = 9, Assists = 9 }, // playoffs
        };

        var totals = PlayerTotalsSource.AggregateRange(lines, "20252026", "2026-01-01", null);

        Assert.Equal(1, totals.GamesPlayed);
        Assert.Equal(1, totals.Goals);
    }
}
