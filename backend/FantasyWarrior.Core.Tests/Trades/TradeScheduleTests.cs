using FantasyWarrior.Core.Periods;
using FantasyWarrior.Core.Trades;

namespace FantasyWarrior.Core.Tests.Trades;

public class TradeScheduleTests
{
    // The 2025-26 calendar the replay actually uses: week 1 anchored on the
    // Monday on or before the first game (2025-10-07), so weeks run Mon..Sun
    // from 2025-10-06. Weeks 9 and 10 are the ones the trade scenario lives in.
    private static readonly PeriodSpan[] Season =
    [
        new(8, new DateOnly(2025, 11, 24), new DateOnly(2025, 11, 30)),
        new(9, new DateOnly(2025, 12, 1), new DateOnly(2025, 12, 7)),
        new(10, new DateOnly(2025, 12, 8), new DateOnly(2025, 12, 14)),
    ];

    [Fact]
    public void MidWeek_LandsOnTheFollowingMonday()
    {
        // Accepted Wednesday of week 9 — the swap is effective when week 10
        // opens, which is the week the GM is setting his lineup for.
        var effective = TradeSchedule.NextPeriodStart(Season, new DateOnly(2025, 12, 3));

        Assert.Equal(new DateOnly(2025, 12, 8), effective);
    }

    [Fact]
    public void FirstDayOfAWeek_StillLandsOnTheNextOne()
    {
        // The Monday a week opens is already inside it: its own start is not
        // "after today", so the trade cannot land on the week now underway.
        // Getting this wrong would move a player into a week whose lineup is
        // already locked.
        var effective = TradeSchedule.NextPeriodStart(Season, new DateOnly(2025, 12, 1));

        Assert.Equal(new DateOnly(2025, 12, 8), effective);
    }

    [Fact]
    public void LastDayOfAWeek_LandsTomorrow()
    {
        // Sunday night. The GM has essentially no time left to set the lineup
        // he is inheriting; the carry-forward rule covers him, and this is the
        // same consequence as any mid-week acquisition.
        var effective = TradeSchedule.NextPeriodStart(Season, new DateOnly(2025, 12, 7));

        Assert.Equal(new DateOnly(2025, 12, 8), effective);
    }

    [Fact]
    public void PastTheLastWeek_HasNoEffectiveDate()
    {
        // Nothing starts after the final week, and opening roster spots into a
        // season with no weeks left to score them would be worse than refusing.
        var effective = TradeSchedule.NextPeriodStart(Season, new DateOnly(2025, 12, 14));

        Assert.Null(effective);
    }

    [Fact]
    public void DeadWeek_IsStillAWeek()
    {
        // The Olympic break scores zero but exists, so a trade lands on it like
        // any other boundary rather than skipping to the week after.
        PeriodSpan[] withBreak =
        [
            new(19, new DateOnly(2026, 2, 2), new DateOnly(2026, 2, 8)),
            new(20, new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 15)),
        ];

        var effective = TradeSchedule.NextPeriodStart(withBreak, new DateOnly(2026, 2, 4));

        Assert.Equal(new DateOnly(2026, 2, 9), effective);
    }

    [Fact]
    public void UnorderedPeriods_StillYieldTheEarliestOne()
    {
        // The caller reads these from the database and nothing forces an ORDER
        // BY. Taking "the first one after today" from an unsorted list would
        // otherwise land the trade an arbitrary number of weeks out.
        var shuffled = Season.Reverse();

        var effective = TradeSchedule.NextPeriodStart(shuffled, new DateOnly(2025, 12, 3));

        Assert.Equal(new DateOnly(2025, 12, 8), effective);
    }

    [Fact]
    public void NoCalendar_HasNoEffectiveDate()
    {
        Assert.Null(TradeSchedule.NextPeriodStart([], new DateOnly(2025, 12, 3)));
    }
}
