using FantasyWarrior.Jobs.Nhl;

namespace FantasyWarrior.Core.Tests.Stats;

public class StatsSyncJobTests
{
    [Theory]
    [InlineData("18:57", 1137)]
    [InlineData("60:00", 3600)]
    [InlineData("65:00", 3900)] // OT
    [InlineData("", 0)]
    [InlineData(null, 0)]
    [InlineData("garbage", 0)]
    public void TimeOnIce_ParsesMinutesSeconds(string? toi, int expected)
        => Assert.Equal(expected, StatsSyncJob.TimeOnIce(toi));

    [Fact]
    public void EnumerateDates_IsInclusive()
    {
        var dates = StatsSyncJob.EnumerateDates(new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 1)).ToList();
        Assert.Equal(
            [new DateOnly(2026, 3, 30), new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 1)],
            dates);
    }

    [Fact]
    public void IsShutout_SoloGoalieZeroGoalsAgainst()
    {
        var goalie = Goalie(goalsAgainst: 0, toi: "60:00");
        Assert.True(StatsSyncJob.IsShutout(goalie, [goalie, Goalie(goalsAgainst: 0, toi: "")]));
    }

    [Fact]
    public void IsShutout_FalseWhenGoalsAllowed()
    {
        var goalie = Goalie(goalsAgainst: 1, toi: "60:00");
        Assert.False(StatsSyncJob.IsShutout(goalie, [goalie]));
    }

    [Fact]
    public void IsShutout_FalseWhenRelieved()
    {
        // Two goalies played: no shutout even with 0 GA each.
        var starter = Goalie(goalsAgainst: 0, toi: "30:00");
        var backup = Goalie(goalsAgainst: 0, toi: "30:00");
        Assert.False(StatsSyncJob.IsShutout(starter, [starter, backup]));
    }

    private static BoxPlayerDto Goalie(int goalsAgainst, string toi) =>
        new() { GoalsAgainst = goalsAgainst, Toi = toi, Position = "G" };
}
