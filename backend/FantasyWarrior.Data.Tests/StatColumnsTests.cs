using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Data.Entities;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The adapter between stored columns and the scorable stat map.
///
/// This is where the migration found a real error in the old data: a goalie's
/// three wins had been recorded as three points instead of six, because the
/// decision field was not fully captured. The rule is one line of code and it
/// is worth pinning down, because getting it wrong is invisible — the totals
/// stay plausible, they are just quietly low.
///
/// Pure: no database, no fixture.
/// </summary>
public class StatColumnsTests
{
    private static PlayerGameStat Line() => new()
    {
        GameId = 1, PlayerId = 2, GameDate = new DateOnly(2025, 10, 8), Season = "20252026",
        GameType = GameType.RegularSeason, TeamAbbrev = "MTL", OpponentAbbrev = "TOR", Position = "C",
    };

    [Fact]
    public void ASkaterLine_CountsTheGameAndEveryCountingStat()
    {
        var line = Line();
        line.Goals = 2; line.Assists = 1; line.PlusMinus = 3; line.Pim = 4;
        line.Shots = 5; line.Hits = 6; line.BlockedShots = 7;

        var stats = StatColumns.ToStatLine(line);

        // The game itself counts, which is what lets a league legitimately score
        // "1 point per game played" with no code change.
        Assert.Equal(1, stats[StatKeys.GamesPlayed]);
        Assert.Equal(2, stats[StatKeys.Goals]);
        Assert.Equal(1, stats[StatKeys.Assists]);
        Assert.Equal(3, stats[StatKeys.PlusMinus]);
        Assert.Equal(4, stats[StatKeys.Pim]);
        Assert.Equal(5, stats[StatKeys.Shots]);
        Assert.Equal(6, stats[StatKeys.Hits]);
        Assert.Equal(7, stats[StatKeys.BlockedShots]);
    }

    [Fact]
    public void AWinIsDerivedFromTheDecisionField()
    {
        var line = Line();
        line.IsGoalie = true;
        line.Decision = "W";
        line.Saves = 30;
        line.ShotsAgainst = 32;
        line.GoalsAgainst = 2;

        var stats = StatColumns.ToStatLine(line);

        // The exact rule the old data got wrong.
        Assert.Equal(1, stats[StatKeys.Wins]);
        Assert.Equal(0, stats[StatKeys.OtLosses]);
        Assert.Equal(30, stats[StatKeys.Saves]);
        Assert.Equal(32, stats[StatKeys.ShotsAgainst]);
        Assert.Equal(2, stats[StatKeys.GoalsAgainst]);
    }

    [Fact]
    public void AnOvertimeLossIsNotAWin()
    {
        var line = Line();
        line.IsGoalie = true;
        line.Decision = "O";
        line.OtLoss = true;

        var stats = StatColumns.ToStatLine(line);

        Assert.Equal(0, stats[StatKeys.Wins]);
        Assert.Equal(1, stats[StatKeys.OtLosses]);
    }

    [Fact]
    public void ARegulationLossScoresNeither()
    {
        var line = Line();
        line.IsGoalie = true;
        line.Decision = "L";

        var stats = StatColumns.ToStatLine(line);

        Assert.Equal(0, stats[StatKeys.Wins]);
        Assert.Equal(0, stats[StatKeys.OtLosses]);
        Assert.Equal(0, stats[StatKeys.Shutouts]);
        // He still played.
        Assert.Equal(1, stats[StatKeys.GamesPlayed]);
    }

    [Fact]
    public void NullSkaterStatsReadAsZero_WithoutLosingTheGamePlayed()
    {
        // A goalie's line has no goals or hits, and that is not missing data.
        var stats = StatColumns.ToStatLine(Line());

        Assert.Equal(1, stats[StatKeys.GamesPlayed]);
        Assert.Equal(0, stats[StatKeys.Goals]);
        Assert.Equal(0, stats[StatKeys.Hits]);
    }

    [Fact]
    public void ThreeGoalieWinsScoreSixUnderTheMordusScale()
    {
        // The Bobrovsky case, verbatim: three starts, three wins, two points a
        // win. The old data reported three games played and three points.
        var scale = new Dictionary<string, double>
        {
            [StatKeys.Goals] = 1, [StatKeys.Assists] = 1,
            [StatKeys.Wins] = 2, [StatKeys.OtLosses] = 1, [StatKeys.Shutouts] = 0,
        };

        var week = Enumerable.Range(0, 3).Select(_ =>
        {
            var line = Line();
            line.IsGoalie = true;
            line.Decision = "W";
            return StatColumns.ToStatLine(line);
        });

        var total = StatLine.Sum(week);

        Assert.Equal(3, total[StatKeys.GamesPlayed]);
        Assert.Equal(3, total[StatKeys.Wins]);
        Assert.Equal(6, total.Score(scale));
    }

    [Fact]
    public void AssignmentColumnsRoundTripThroughTheStatMap()
    {
        var assignment = new RosterAssignment();
        var original = new StatLine(new Dictionary<string, int>
        {
            [StatKeys.GamesPlayed] = 4, [StatKeys.Goals] = 3, [StatKeys.Assists] = 5,
            [StatKeys.Wins] = 2, [StatKeys.Shutouts] = 1, [StatKeys.Saves] = 88,
        });

        StatColumns.Apply(assignment, original);
        var read = StatColumns.ToStatLine(assignment);

        Assert.Equal(4, read[StatKeys.GamesPlayed]);
        Assert.Equal(3, read[StatKeys.Goals]);
        Assert.Equal(5, read[StatKeys.Assists]);
        Assert.Equal(2, read[StatKeys.Wins]);
        Assert.Equal(1, read[StatKeys.Shutouts]);
        Assert.Equal(88, read[StatKeys.Saves]);
    }

    [Fact]
    public void ApplyWritesZeroes_SoLastWeeksNumbersCannotSurvive()
    {
        // A rescore that only assigned non-zero stats would leave a player who
        // scored last week and not this one showing last week's goals.
        var assignment = new RosterAssignment();
        StatColumns.Apply(assignment, new StatLine(new Dictionary<string, int> { [StatKeys.Goals] = 5 }));
        Assert.Equal(5, assignment.Goals);

        StatColumns.Apply(assignment, StatLine.Empty);

        Assert.Equal(0, assignment.Goals);
        Assert.Equal(0, assignment.GamesPlayed);
    }
}
