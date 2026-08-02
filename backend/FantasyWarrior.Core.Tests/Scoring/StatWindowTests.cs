using FantasyWarrior.Core.Scoring;
namespace FantasyWarrior.Core.Tests.Scoring;

public class StatWindowTests
{
    // --- Intersect: which days of a period a roster spot actually owns ---
    // A week: Monday 2026-01-05 through Sunday 2026-01-11.

    private static readonly DateOnly Start = D("2026-01-05");
    private static readonly DateOnly End = D("2026-01-11");
    private static readonly DateOnly Synced = D("2026-01-11"); // whole week already synced

    /// <summary>
    /// The expected window, written the way the test dates read. Windows now
    /// carry real dates rather than "YYYY-MM-DD" strings — that was a Firestore
    /// constraint, since ordinal string comparison was the only way to range-query
    /// a single field. The rule these tests pin down did not change.
    /// </summary>
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static DateWindow Window(DateOnly from, DateOnly to) => new(from, to);

    [Fact]
    public void Intersect_SpotOpenAllWeek_OwnsTheWholeWeek()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: D("2025-10-01"), spotEnd: null, Synced);

        Assert.Equal(Window(Start, End), w);
    }

    [Fact]
    public void Intersect_SpotOpenedMidWeek_StartsWhenTheSpotDid()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: D("2026-01-08"), spotEnd: null, Synced);

        Assert.Equal(Window(D("2026-01-08"), End), w);
    }

    [Fact]
    public void Intersect_SpotClosedMidWeek_KeepsOnlyTheDaysItWasHeld()
    {
        // The traded-away player: his old team keeps Mon-Wed and nothing after.
        var w = StatWindow.Intersect(Start, End, spotStart: D("2025-10-01"), spotEnd: D("2026-01-07"), Synced);

        Assert.Equal(Window(Start, D("2026-01-07")), w);
    }

    [Fact]
    public void Intersect_SpotEntirelyInsideTheWeek()
    {
        var w = StatWindow.Intersect(Start, End, spotStart: D("2026-01-06"), spotEnd: D("2026-01-09"), Synced);

        Assert.Equal(Window(D("2026-01-06"), D("2026-01-09")), w);
    }

    [Fact]
    public void Intersect_SpotClosedBeforeThePeriod_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, D("2025-10-01"), spotEnd: D("2026-01-04"), Synced));
    }

    [Fact]
    public void Intersect_SpotOpensAfterThePeriod_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, spotStart: D("2026-01-12"), spotEnd: null, Synced));
    }

    [Fact]
    public void Intersect_MidWeekRun_ClampsToTheLastSyncedDay()
    {
        // Scoring past lastStatDate would bank a zero for days whose boxscores
        // have not been written yet, and never revisit them.
        var w = StatWindow.Intersect(Start, End, D("2025-10-01"), null, lastStatDate: D("2026-01-07"));

        Assert.Equal(Window(Start, D("2026-01-07")), w);
    }

    [Fact]
    public void Intersect_PeriodNotStartedYet_OwnsNothing()
    {
        Assert.Null(StatWindow.Intersect(Start, End, D("2025-10-01"), null, lastStatDate: D("2026-01-04")));
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
        var w = StatWindow.Intersect(Start, End, D("2025-10-01"), D("2026-01-07"), lastStatDate: D("2026-02-01"));

        Assert.Equal(Window(Start, D("2026-01-07")), w);
    }
}
