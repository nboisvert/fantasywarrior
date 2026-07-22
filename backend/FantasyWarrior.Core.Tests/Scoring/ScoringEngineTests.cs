using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

public class ScoringEngineTests
{
    private static readonly RuleConfig Defaults = new();

    [Fact]
    public void PlayerPoints_UsesDefaultValues()
    {
        // 2G 3A skater = 5; goalie 1W 1OTL 1SO = 2+1+0 = 3
        Assert.Equal(5, ScoringEngine.PlayerPoints(new PlayerRawTotals(Goals: 2, Assists: 3), Defaults.PointValues));
        Assert.Equal(3, ScoringEngine.PlayerPoints(new PlayerRawTotals(Wins: 1, OtLosses: 1, Shutouts: 1), Defaults.PointValues));
    }

    [Fact]
    public void PlayerPoints_CustomValues()
    {
        var values = new PointValues { Goal = 2, Assist = 1, GoalieWin = 2, GoalieOtLoss = 1, Shutout = 3 };
        Assert.Equal(7, ScoringEngine.PlayerPoints(new PlayerRawTotals(Goals: 3, Assists: 1), values));
        Assert.Equal(5, ScoringEngine.PlayerPoints(new PlayerRawTotals(Wins: 1, Shutouts: 1), values));
    }

    [Fact]
    public void TeamScore_NoTopCount_CountsEveryone()
    {
        var result = ScoringEngine.TeamScore(
        [
            new RosterEntry(1, "C", new PlayerRawTotals(Goals: 2)),
            new RosterEntry(2, "D", new PlayerRawTotals(Assists: 1)),
            new RosterEntry(3, "G", new PlayerRawTotals(Wins: 3)),
        ], Defaults);

        Assert.Equal(2 + 1 + 6, result.RawTopX);
        Assert.Equal([1, 2, 3], result.CountedPlayerIds);
    }

    [Fact]
    public void TeamScore_TopXPerGroup()
    {
        var config = new RuleConfig { TopCount = new TopCount { Forwards = 2, Defense = 1, Goalies = 1 } };
        var result = ScoringEngine.TeamScore(
        [
            new RosterEntry(1, "C", new PlayerRawTotals(Goals: 5)),  // F 5 — counted
            new RosterEntry(2, "L", new PlayerRawTotals(Goals: 3)),  // F 3 — counted
            new RosterEntry(3, "R", new PlayerRawTotals(Goals: 1)),  // F 1 — dropped
            new RosterEntry(4, "D", new PlayerRawTotals(Goals: 2)),  // D 2 — counted
            new RosterEntry(5, "D", new PlayerRawTotals(Goals: 4)),  // D 4? no: 4 > 2 — counted instead
            new RosterEntry(6, "G", new PlayerRawTotals(Wins: 2)),   // G 4 — counted
            new RosterEntry(7, "G", new PlayerRawTotals(Wins: 1)),   // G 2 — dropped
        ], config);

        Assert.Equal(5 + 3 + 4 + 4, result.RawTopX);
        Assert.Equal([1, 2, 5, 6], result.CountedPlayerIds);
    }

    [Fact]
    public void TeamScore_TieBrokenByPlayerIdAscending()
    {
        var config = new RuleConfig { TopCount = new TopCount { Forwards = 1 } };
        var result = ScoringEngine.TeamScore(
        [
            new RosterEntry(20, "C", new PlayerRawTotals(Goals: 3)),
            new RosterEntry(10, "C", new PlayerRawTotals(Goals: 3)),
        ], config);

        Assert.Equal([10], result.CountedPlayerIds);
    }

    [Fact]
    public void TeamScore_PlayerWithoutStats_CountsAsZero()
    {
        var result = ScoringEngine.TeamScore([new RosterEntry(1, "C", new PlayerRawTotals())], Defaults);
        Assert.Equal(0, result.RawTopX);
        Assert.Equal([1], result.CountedPlayerIds);
    }

    [Fact]
    public void TransactionAdjustment_KeepsScoreInvariant()
    {
        var config = new RuleConfig { TopCount = new TopCount { Forwards = 1 } };
        var mcdavid = new RosterEntry(97, "C", new PlayerRawTotals(Goals: 50, Assists: 30)); // 80 pts
        var crosby = new RosterEntry(87, "C", new PlayerRawTotals(Goals: 40, Assists: 55)); // 95 pts

        var before = ScoringEngine.TeamScore([mcdavid], config);
        var after = ScoringEngine.TeamScore([crosby], config);
        var adjustment = ScoringEngine.TransactionAdjustment(before.RawTopX, after.RawTopX);

        // displayed score = rawTopX + adjustments must not move at trade time
        Assert.Equal(before.RawTopX, after.RawTopX + adjustment);
        Assert.Equal(-15, adjustment);
    }
}
