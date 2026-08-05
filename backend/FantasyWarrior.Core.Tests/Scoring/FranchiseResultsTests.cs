using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

public class FranchiseResultsTests
{
    private static GameResult Game(
        string home, string away, int homeScore, int awayScore, string? ending = "REG") =>
        new(home, away, homeScore, awayScore, ending);

    [Fact]
    public void AHomeWin_CountsAsAWin()
    {
        var line = FranchiseResults.For("MTL", [Game("MTL", "TOR", 4, 2)]);

        Assert.Equal(1, line[StatKeys.TeamWins]);
        Assert.Equal(0, line[StatKeys.TeamLosses]);
        Assert.Equal(0, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void AnAwayWin_CountsAsAWin()
    {
        // Same game from the other bench: which side of the row a franchise
        // sits on is bookkeeping, not a result.
        var line = FranchiseResults.For("TOR", [Game("MTL", "TOR", 2, 4)]);

        Assert.Equal(1, line[StatKeys.TeamWins]);
        Assert.Equal(0, line[StatKeys.TeamLosses]);
    }

    [Fact]
    public void ARegulationLoss_CountsAsALoss()
    {
        var line = FranchiseResults.For("TOR", [Game("MTL", "TOR", 4, 2)]);

        Assert.Equal(0, line[StatKeys.TeamWins]);
        Assert.Equal(1, line[StatKeys.TeamLosses]);
        Assert.Equal(0, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void AnOvertimeLoss_IsNotARegulationLoss()
    {
        var line = FranchiseResults.For("TOR", [Game("MTL", "TOR", 3, 2, "OT")]);

        Assert.Equal(0, line[StatKeys.TeamLosses]);
        Assert.Equal(1, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void AShootoutLoss_CountsWithTheOvertimeLosses()
    {
        // The NHL pays a shootout loss the same point it pays an overtime one,
        // and so does the pool: one bucket, not two.
        var line = FranchiseResults.For("TOR", [Game("MTL", "TOR", 3, 2, "SO")]);

        Assert.Equal(1, line[StatKeys.TeamOtLosses]);
        Assert.Equal(0, line[StatKeys.TeamLosses]);
    }

    [Fact]
    public void WinningInOvertime_IsStillJustAWin()
    {
        var line = FranchiseResults.For("MTL", [Game("MTL", "TOR", 3, 2, "OT")]);

        Assert.Equal(1, line[StatKeys.TeamWins]);
        Assert.Equal(0, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void AGameBetweenTwoOtherTeams_ContributesNothing()
    {
        // The caller hands over the whole week's schedule rather than filtering
        // it once per franchise, so this is the common case, not an edge one.
        var line = FranchiseResults.For("MTL", [Game("BOS", "TOR", 5, 1)]);

        Assert.Equal(0, line[StatKeys.TeamWins]);
        Assert.Equal(0, line[StatKeys.TeamLosses]);
        Assert.Equal(0, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void AGameWithNoOutcomeYet_IsNotALossForBothTeams()
    {
        // **The one that would have quietly corrupted a season.** A scheduled,
        // in-progress or postponed game carries 0-0 and no outcome; reading the
        // score alone would hand both franchises a regulation loss every night
        // for every game still to come.
        var scheduled = new GameResult("MTL", "TOR", 0, 0, null);

        var mtl = FranchiseResults.For("MTL", [scheduled]);
        var tor = FranchiseResults.For("TOR", [scheduled]);

        Assert.Equal(0, mtl[StatKeys.TeamLosses]);
        Assert.Equal(0, tor[StatKeys.TeamLosses]);
        Assert.Equal(0, mtl[StatKeys.TeamWins]);
    }

    [Fact]
    public void AWeekOfGames_AddsUp()
    {
        var line = FranchiseResults.For("MTL",
        [
            Game("MTL", "TOR", 4, 2),           // W
            Game("BOS", "MTL", 1, 3),           // W
            Game("MTL", "OTT", 2, 3, "OT"),     // OTL
            Game("NYR", "MTL", 5, 0),           // L
            Game("BOS", "TOR", 2, 1),           // not ours
        ]);

        Assert.Equal(2, line[StatKeys.TeamWins]);
        Assert.Equal(1, line[StatKeys.TeamLosses]);
        Assert.Equal(1, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void NoGames_IsAllZeroes()
    {
        // A bye week, or a franchise slot opened after the last synced day.
        var line = FranchiseResults.For("MTL", []);

        Assert.Equal(0, line[StatKeys.TeamWins]);
        Assert.Equal(0, line[StatKeys.TeamLosses]);
        Assert.Equal(0, line[StatKeys.TeamOtLosses]);
    }

    [Fact]
    public void TheRecord_CarriesNoGamesPlayed()
    {
        // Nick, 2026-08-05: the Équipe slot reports a record, not a workload.
        // It is also what keeps vStandings.RosterGamesPlayed -- the denominator
        // under every points-per-game figure in the app -- about players.
        var line = FranchiseResults.For("MTL", [Game("MTL", "TOR", 4, 2)]);

        Assert.Equal(0, line[StatKeys.GamesPlayed]);
    }

    [Fact]
    public void TheRecord_ScoresUnderTheLeaguesOwnScale()
    {
        // Les Mordus: 2 per win, 1 per overtime loss, nothing for a loss. The
        // franchise goes through the same StatLine.Score as every player, which
        // is the entire point of giving it stat keys rather than its own engine.
        var line = FranchiseResults.For("MTL",
        [
            Game("MTL", "TOR", 4, 2),
            Game("MTL", "OTT", 2, 3, "SO"),
            Game("NYR", "MTL", 5, 0),
        ]);

        var scale = new Dictionary<string, double>
        {
            [StatKeys.TeamWins] = 2,
            [StatKeys.TeamOtLosses] = 1,
            [StatKeys.TeamLosses] = 0,
        };

        Assert.Equal(3, line.Score(scale));
    }
}
