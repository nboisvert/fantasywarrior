using FantasyWarrior.Core.Periods;

namespace FantasyWarrior.Core.Tests.Periods;

public class PeriodCalendarTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    [Fact]
    public void MondayOnOrBefore_IsIdentityOnAMonday()
    {
        // 2025-10-06 is a Monday.
        Assert.Equal(D("2025-10-06"), PeriodCalendar.MondayOnOrBefore(D("2025-10-06")));
    }

    [Fact]
    public void MondayOnOrBefore_WalksBackFromEveryOtherDay()
    {
        Assert.Equal(D("2025-10-06"), PeriodCalendar.MondayOnOrBefore(D("2025-10-07"))); // Tue
        Assert.Equal(D("2025-10-06"), PeriodCalendar.MondayOnOrBefore(D("2025-10-11"))); // Sat
        // Sunday is the END of its week, so it walks back six days, not forward one.
        Assert.Equal(D("2025-10-06"), PeriodCalendar.MondayOnOrBefore(D("2025-10-12"))); // Sun
    }

    [Fact]
    public void Generate_AnchorsWeekOneOnTheMondayBeforeTheFirstGame()
    {
        // The real 2025-26 season opened Tuesday 2025-10-07.
        var weeks = PeriodCalendar.Generate(D("2025-10-07"), D("2025-10-20"));

        Assert.Equal(1, weeks[0].Index);
        Assert.Equal(D("2025-10-06"), weeks[0].Start); // Monday
        Assert.Equal(D("2025-10-12"), weeks[0].End);   // Sunday
    }

    [Fact]
    public void Generate_ProducesContiguousSevenDayWeeks()
    {
        var weeks = PeriodCalendar.Generate(D("2025-10-07"), D("2026-04-16"));

        for (var i = 1; i < weeks.Count; i++)
        {
            Assert.Equal(weeks[i - 1].End.AddDays(1), weeks[i].Start);
            Assert.Equal(6, weeks[i].End.DayNumber - weeks[i].Start.DayNumber);
            Assert.Equal(weeks[i - 1].Index + 1, weeks[i].Index);
        }
    }

    [Fact]
    public void Generate_CoversTheRealSeasonInTwentyEightWeeks()
    {
        // 2025-10-07 -> 2026-04-16, the boundaries already confirmed in Firestore.
        var weeks = PeriodCalendar.Generate(D("2025-10-07"), D("2026-04-16"));

        Assert.Equal(28, weeks.Count);
        Assert.Equal(D("2025-10-06"), weeks[0].Start);
        Assert.True(weeks[^1].Start <= D("2026-04-16"));
    }

    [Fact]
    public void Generate_LastWeekMayRunPastTheFinalGame()
    {
        // A season rarely ends on a Sunday; the final week is simply short of
        // games rather than short of days.
        var weeks = PeriodCalendar.Generate(D("2025-10-07"), D("2026-04-16")); // Thursday

        Assert.True(weeks[^1].End >= D("2026-04-16"));
    }

    [Fact]
    public void Generate_HandlesASeasonInsideASingleWeek()
    {
        var weeks = PeriodCalendar.Generate(D("2025-10-07"), D("2025-10-09"));

        Assert.Single(weeks);
    }

    [Fact]
    public void Generate_ReturnsNothingWhenTheRangeIsInverted()
    {
        Assert.Empty(PeriodCalendar.Generate(D("2026-04-16"), D("2025-10-07")));
    }

    [Fact]
    public void IndexFor_CountsWeeksFromTheAnchor()
    {
        var anchor = D("2025-10-06");

        Assert.Equal(1, PeriodCalendar.IndexFor(anchor, D("2025-10-06")));
        Assert.Equal(1, PeriodCalendar.IndexFor(anchor, D("2025-10-12"))); // Sunday, still week 1
        Assert.Equal(2, PeriodCalendar.IndexFor(anchor, D("2025-10-13"))); // Monday, week 2
        Assert.Equal(3, PeriodCalendar.IndexFor(anchor, D("2025-10-20")));
    }

    [Fact]
    public void IndexFor_ReturnsNullBeforeTheSeason()
    {
        Assert.Null(PeriodCalendar.IndexFor(D("2025-10-06"), D("2025-10-05")));
    }

    [Fact]
    public void ALateSundayGameBelongsToTheSundayWeekNotTheNextOne()
    {
        // A West Coast game starting Sunday 22:30 ET ends after midnight, but the
        // NHL stamps it with the Sunday gameDate -- which is what we index on.
        // Getting this wrong would move a whole game's points into the next week.
        var anchor = D("2025-10-06");

        Assert.Equal(1, PeriodCalendar.IndexFor(anchor, D("2025-10-12")));
        Assert.Equal(2, PeriodCalendar.IndexFor(anchor, D("2025-10-13")));
    }


    [Fact]
    public void LockUtcFor_IsMidnightEasternNotMidnightUtc()
    {
        // October is EDT (UTC-4): midnight ET is 04:00 UTC the same day.
        Assert.Equal(new DateTime(2025, 10, 6, 4, 0, 0, DateTimeKind.Utc), PeriodCalendar.LockUtcFor(D("2025-10-06")));

        // January is EST (UTC-5): 05:00 UTC.
        Assert.Equal(new DateTime(2026, 1, 5, 5, 0, 0, DateTimeKind.Utc), PeriodCalendar.LockUtcFor(D("2026-01-05")));
    }
}
