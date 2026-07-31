using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Tests.Scoring;

public class StatLineAdapterTests
{
    [Fact]
    public void RawTotals_RoundTripsThroughStatLine()
    {
        // Distinct values per field: a mis-wired mapping would swap two of them
        // and still pass if they shared a value. PlayerRawTotals' constructor is
        // positional, which is exactly the mistake this guards.
        var original = new PlayerRawTotals(
            GamesPlayed: 82, Goals: 40, Assists: 55, Wins: 3, OtLosses: 4, Shutouts: 5,
            PlusMinus: 6, Pim: 7, Shots: 8, Hits: 9, BlockedShots: 10,
            GoalsAgainst: 11, Saves: 12, ShotsAgainst: 13);

        Assert.Equal(original, original.ToStatLine().ToRawTotals());
    }

    [Fact]
    public void RawTotals_MapsEachFieldToItsOwnKey()
    {
        var stats = new PlayerRawTotals(GamesPlayed: 82, Goals: 40, Assists: 55, Wins: 3).ToStatLine();

        Assert.Equal(82, stats[StatKeys.GamesPlayed]);
        Assert.Equal(40, stats[StatKeys.Goals]);
        Assert.Equal(55, stats[StatKeys.Assists]);
        Assert.Equal(3, stats[StatKeys.Wins]);
    }

    [Fact]
    public void SeasonStatsCache_ConvertsInMemory()
    {
        var cached = new PlayerSeasonStats
        {
            Season = "20252026", PlayerId = 8478402,
            GamesPlayed = 82, Goals = 40, Assists = 55, PlusMinus = 6, Pim = 7,
            Shots = 8, Hits = 9, BlockedShots = 10,
            Wins = 3, OtLosses = 4, Shutouts = 5, GoalsAgainst = 11, Saves = 12, ShotsAgainst = 13,
        };

        var stats = cached.ToStatLine();

        Assert.Equal(82, stats[StatKeys.GamesPlayed]);
        Assert.Equal(40, stats[StatKeys.Goals]);
        Assert.Equal(13, stats[StatKeys.ShotsAgainst]);
    }

    [Fact]
    public void ScoringAStatLineMatchesTheLegacyEngine()
    {
        // Same inputs, same league scale, same answer -- the property that lets
        // the two paths run side by side during the cutover.
        var totals = new PlayerRawTotals(Goals: 3, Assists: 2, Wins: 1, OtLosses: 1, Shutouts: 1);
        var legacy = new PointValues { Goal = 1, Assist = 1, GoalieWin = 1, GoalieOtLoss = 1, Shutout = 0 };

        var viaEngine = ScoringEngine.PlayerPoints(totals, legacy);
        var viaStatLine = totals.ToStatLine().Score(new Dictionary<string, double>
        {
            [StatKeys.Goals] = legacy.Goal,
            [StatKeys.Assists] = legacy.Assist,
            [StatKeys.Wins] = legacy.GoalieWin,
            [StatKeys.OtLosses] = legacy.GoalieOtLoss,
            [StatKeys.Shutouts] = legacy.Shutout,
        });

        Assert.Equal(viaEngine, viaStatLine);
        Assert.Equal(7, viaStatLine);
    }
}
