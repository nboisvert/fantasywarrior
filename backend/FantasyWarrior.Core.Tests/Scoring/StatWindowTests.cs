using FantasyWarrior.Core.Scoring;
using FantasyWarrior.Core.Stats;

namespace FantasyWarrior.Core.Tests.Scoring;

public class StatWindowTests
{
    private static PlayerGameStats Line(string date, int goals, int assists) => new()
    {
        Date = date,
        Season = "20252026",
        GameType = 2,
        Goals = goals,
        Assists = assists,
    };

    // --- Sum: ported verbatim from PlayerTotalsSourceTests.AggregateRange ---

    [Fact]
    public void Sum_KeepsOnlyLinesWithinInclusiveBounds()
    {
        var lines = new[]
        {
            Line("2026-01-01", goals: 1, assists: 0), // before range
            Line("2026-01-10", goals: 2, assists: 1), // in range (boundary)
            Line("2026-01-15", goals: 1, assists: 1), // in range
            Line("2026-01-20", goals: 5, assists: 5), // in range (boundary)
            Line("2026-01-25", goals: 9, assists: 9), // after range
        };

        var totals = StatWindow.Sum(lines, "20252026", "2026-01-10", "2026-01-20");

        Assert.Equal(3, totals[StatKeys.GamesPlayed]);
        Assert.Equal(8, totals[StatKeys.Goals]);   // 2+1+5
        Assert.Equal(7, totals[StatKeys.Assists]); // 1+1+5
    }

    [Fact]
    public void Sum_NullToMeansStillOpen_IncludesEverythingFromStart()
    {
        var lines = new[] { Line("2026-01-05", 1, 0), Line("2026-03-01", 2, 2) };

        var totals = StatWindow.Sum(lines, "20252026", "2026-01-01", to: null);

        Assert.Equal(2, totals[StatKeys.GamesPlayed]);
        Assert.Equal(3, totals[StatKeys.Goals]);
    }

    [Fact]
    public void Sum_IgnoresOtherSeasonsAndPlayoffs()
    {
        var lines = new[]
        {
            Line("2026-01-10", 1, 1),
            new PlayerGameStats { Date = "2026-01-11", Season = "20242025", GameType = 2, Goals = 9, Assists = 9 },
            new PlayerGameStats { Date = "2026-01-12", Season = "20252026", GameType = 3, Goals = 9, Assists = 9 },
        };

        var totals = StatWindow.Sum(lines, "20252026", "2026-01-01", null);

        Assert.Equal(1, totals[StatKeys.GamesPlayed]);
        Assert.Equal(1, totals[StatKeys.Goals]);
    }

    // --- Intersect: which days of a period a roster spot actually owns ---
    // A week: Monday 2026-01-05 through Sunday 2026-01-11.

    private const string Start = "2026-01-05";
    private const string End = "2026-01-11";
    private const string Synced = "2026-01-11"; // whole week already synced

    /// <summary>
    /// The expected window, written the way the test dates read. Windows now
    /// carry real dates rather than "YYYY-MM-DD" strings — that was a Firestore
    /// constraint, since ordinal string comparison was the only way to range-query
    /// a single field. The rule these tests pin down did not change.
    /// </summary>
    private static DateWindow Window(string from, string to) =>
        new(DateOnly.Parse(from), DateOnly.Parse(to));

    [Fact]
    public void Intersect_SpotOpenAllWeek_OwnsTheWholeWeek()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: "2025-10-01", spotEnd: null, Synced);

        Assert.Equal(Window(Start, End), w);
    }

    [Fact]
    public void Intersect_SpotOpenedMidWeek_StartsWhenTheSpotDid()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: "2026-01-08", spotEnd: null, Synced);

        Assert.Equal(Window("2026-01-08", End), w);
    }

    [Fact]
    public void Intersect_SpotClosedMidWeek_KeepsOnlyTheDaysItWasHeld()
    {
        // The traded-away player: his old team keeps Mon-Wed and nothing after.
        var w = StatWindow.Intersect(Start, End, spotStart: "2025-10-01", spotEnd: "2026-01-07", Synced);

        Assert.Equal(Window(Start, "2026-01-07"), w);
    }

    [Fact]
    public void Intersect_SpotEntirelyInsideTheWeek()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: "2026-01-06", spotEnd: "2026-01-09", Synced);

        Assert.Equal(Window("2026-01-06", "2026-01-09"), w);
    }

    [Fact]
    public void Intersect_SpotClosedBeforeThePeriod_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, "2025-10-01", spotEnd: "2026-01-04", Synced));
    }

    [Fact]
    public void Intersect_SpotOpensAfterThePeriod_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, spotStart: "2026-01-12", spotEnd: null, Synced));
    }

    [Fact]
    public void Intersect_MidWeekRun_ClampsToTheLastSyncedDay()
    {
        // Scoring past lastStatDate would bank a zero for days whose boxscores
        // have not been written yet, and never revisit them.
        var w = StatWindow.Intersect(Start, End, "2025-10-01", null, lastStatDate: "2026-01-07");

        Assert.Equal(Window(Start, "2026-01-07"), w);
    }

    [Fact]
    public void Intersect_PeriodNotStartedYet_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, "2025-10-01", null, lastStatDate: "2026-01-04"));
    }

    [Fact]
    public void Intersect_SingleDayWindowIsValid()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: Start, spotEnd: Start, Synced);

        Assert.Equal(Window(Start, Start), w);
    }

    [Fact]
    public void Intersect_ClosedSpotIsUnaffectedByALaterSyncDate()
    {
        // A spot closed last week keeps its own end date, not the sync date.
        var w = StatWindow.Intersect(Start, End, "2025-10-01", "2026-01-07", lastStatDate: "2026-02-01");

        Assert.Equal(Window(Start, "2026-01-07"), w);
    }
}
