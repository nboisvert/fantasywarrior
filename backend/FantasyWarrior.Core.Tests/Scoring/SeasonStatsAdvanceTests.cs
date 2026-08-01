using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

/// <summary>
/// Getting this wrong is silent: adding a day twice inflates a player's season
/// permanently, and rebuilding when adding would do costs a full rescan per
/// player per day.
/// </summary>
public class SeasonStatsAdvanceTests
{
    [Fact]
    public void NoStoredCursor_Rebuilds()
    {
        // A document written before throughDate existed, or by a real (unbounded)
        // run. Its number cannot be reconciled with a range, so it is replaced.
        Assert.Equal(SeasonStatsAction.Rebuild, SeasonStatsAdvance.Decide(null, "2025-11-23"));
    }

    [Fact]
    public void AlreadyAtTheTarget_Skips()
    {
        Assert.Equal(SeasonStatsAction.Skip, SeasonStatsAdvance.Decide("2025-11-23", "2025-11-23"));
    }

    [Fact]
    public void PastTheTarget_Skips()
    {
        // Forward-only simulation: a cursor ahead of the target means another
        // run already covered this. Adding would double-count.
        Assert.Equal(SeasonStatsAction.Skip, SeasonStatsAdvance.Decide("2025-12-01", "2025-11-23"));
    }

    [Fact]
    public void BehindTheTarget_Adds()
    {
        Assert.Equal(SeasonStatsAction.Add, SeasonStatsAdvance.Decide("2025-11-16", "2025-11-23"));
    }

    [Fact]
    public void RangeToAdd_StartsTheDayAfterWhatIsCounted()
    {
        // Starting on the stored date itself would recount that day — the exact
        // double-count this exists to prevent.
        Assert.Equal(("2025-11-17", "2025-11-23"), SeasonStatsAdvance.RangeToAdd("2025-11-16", "2025-11-23"));
    }

    [Fact]
    public void RangeToAdd_IsASingleDayWhenAdvancingOneDay()
    {
        Assert.Equal(("2025-11-23", "2025-11-23"), SeasonStatsAdvance.RangeToAdd("2025-11-22", "2025-11-23"));
    }

    [Fact]
    public void RangeToAdd_IsNullWhenNothingToAdd()
    {
        Assert.Null(SeasonStatsAdvance.RangeToAdd("2025-11-23", "2025-11-23"));
        Assert.Null(SeasonStatsAdvance.RangeToAdd("2025-12-01", "2025-11-23"));
        Assert.Null(SeasonStatsAdvance.RangeToAdd(null, "2025-11-23"));
    }

    [Fact]
    public void RangeToAdd_CrossesAMonthBoundary()
    {
        Assert.Equal(("2025-12-01", "2025-12-03"), SeasonStatsAdvance.RangeToAdd("2025-11-30", "2025-12-03"));
    }

    [Fact]
    public void TwoConsecutiveRangesSumToTheWholeSpan()
    {
        // The property the incremental design rests on: advancing in steps must
        // land on the same totals as one jump.
        var week1 = new PlayerRawTotals(GamesPlayed: 3, Goals: 2, Assists: 1);
        var week2 = new PlayerRawTotals(GamesPlayed: 4, Goals: 1, Assists: 3);

        var stepwise = SeasonStatsAdvance.Plus(SeasonStatsAdvance.Plus(new PlayerRawTotals(), week1), week2);

        Assert.Equal(7, stepwise.GamesPlayed);
        Assert.Equal(3, stepwise.Goals);
        Assert.Equal(4, stepwise.Assists);
    }

    [Fact]
    public void Plus_AddsEveryCounter()
    {
        var a = new PlayerRawTotals(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14);
        var b = new PlayerRawTotals(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);

        var sum = SeasonStatsAdvance.Plus(a, b);

        Assert.Equal(new PlayerRawTotals(2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15), sum);
    }

    [Fact]
    public void Plus_WithZeroIsIdentity()
    {
        var totals = new PlayerRawTotals(GamesPlayed: 82, Goals: 40, Assists: 55);

        Assert.Equal(totals, SeasonStatsAdvance.Plus(totals, new PlayerRawTotals()));
    }
}
