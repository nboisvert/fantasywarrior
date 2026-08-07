using FantasyWarrior.Data.Entities;
using FantasyWarrior.Data.Rosters;

namespace FantasyWarrior.Data.Tests;

/// <summary>
/// The date arithmetic behind "is this player mine".
///
/// It used to be one question with one answer (<c>EndDate IS NULL</c>). Dating
/// a trade forward split it into three that disagree for exactly the two spots
/// a trade creates — the one leaving Sunday and the one arriving Monday — which
/// is precisely the pair the lineup screen was getting wrong.
///
/// Pure: the expressions are compiled and run over plain objects, no database.
/// </summary>
public class RosterWindowTests
{
    // Week 9 of the replay calendar: Mon 2025-12-01 .. Sun 2025-12-07.
    private static readonly DateOnly WeekStart = new(2025, 12, 1);
    private static readonly DateOnly Wednesday = new(2025, 12, 3);
    private static readonly DateOnly WeekEnd = new(2025, 12, 7);
    private static readonly DateOnly NextWeekStart = new(2025, 12, 8);

    private static RosterSpot Spot(DateOnly start, DateOnly? end = null) =>
        new() { PositionGroup = "F", PlayerId = 8478402, StartDate = start, EndDate = end };

    /// <summary>Held since the draft and going nowhere — the ordinary case.</summary>
    private static RosterSpot Held() => Spot(new DateOnly(2025, 10, 6));

    /// <summary>Traded away Wednesday: last day held is the Sunday, gone Monday.</summary>
    private static RosterSpot Leaving() => Spot(new DateOnly(2025, 10, 6), WeekEnd);

    /// <summary>Acquired in the same trade: his stint opens next Monday.</summary>
    private static RosterSpot Arriving() => Spot(NextWeekStart);

    /// <summary>Released back in October; his points stay, he does not.</summary>
    private static RosterSpot Departed() => Spot(new DateOnly(2025, 10, 6), new DateOnly(2025, 11, 2));

    // --- OwnedOn: today's roster, and today's cap ---

    [Fact]
    public void OwnedToday_CountsTheManLeavingSundayAndNotTheOneArrivingMonday()
    {
        var owned = RosterWindow.OwnedOn(Wednesday).Compile();

        Assert.True(owned(Held()));
        // Still scoring for this team all week — benching him would hand the GM
        // a slot he cannot fill.
        Assert.True(owned(Leaving()));
        // On the books, not on the ice. Charging his cap hit now would show the
        // GM a number he cannot act on.
        Assert.False(owned(Arriving()));
        Assert.False(owned(Departed()));
    }

    [Fact]
    public void OwnedOn_IsInclusiveAtBothEnds()
    {
        var single = Spot(Wednesday, Wednesday);

        Assert.True(RosterWindow.OwnedOn(Wednesday).Compile()(single));
        Assert.False(RosterWindow.OwnedOn(Wednesday.AddDays(-1)).Compile()(single));
        Assert.False(RosterWindow.OwnedOn(Wednesday.AddDays(1)).Compile()(single));
    }

    [Fact]
    public void TheHandover_LeavesNoDayOwnedTwice()
    {
        // The property the whole banked-points model rests on: on no single day
        // do both stints own the player. RosterChange closes the outgoing spot
        // the day *before* the incoming one opens, and this is what says so.
        var leaving = RosterWindow.OwnedOn(NextWeekStart).Compile();
        var arriving = RosterWindow.OwnedOn(WeekEnd).Compile();

        Assert.False(leaving(Leaving()));
        Assert.False(arriving(Arriving()));
    }

    // --- OwnsPeriod: the week's lineup ---

    [Fact]
    public void ThisWeek_HasTheLeaverAndNotTheArrival()
    {
        var owns = RosterWindow.OwnsPeriod(WeekStart, WeekEnd).Compile();

        Assert.True(owns(Held()));
        Assert.True(owns(Leaving()));
        Assert.False(owns(Arriving()));
    }

    [Fact]
    public void NextWeek_HasTheArrivalAndNotTheLeaver()
    {
        // This is the swap the lineup picker was blind to: for the week it is
        // actually writing, the two men have exchanged places.
        var owns = RosterWindow.OwnsPeriod(NextWeekStart, NextWeekStart.AddDays(6)).Compile();

        Assert.True(owns(Held()));
        Assert.False(owns(Leaving()));
        Assert.True(owns(Arriving()));
    }

    [Fact]
    public void AMidWeekStint_StillOwnsThatWeek()
    {
        // Partial ownership is ownership: a spot that opened Thursday scores
        // Thursday through Sunday, and dropping it would sag the team's total.
        var owns = RosterWindow.OwnsPeriod(WeekStart, WeekEnd).Compile();

        Assert.True(owns(Spot(new DateOnly(2025, 12, 4))));
        Assert.True(owns(Spot(new DateOnly(2025, 10, 6), new DateOnly(2025, 12, 2))));
        Assert.False(owns(Spot(new DateOnly(2025, 10, 6), new DateOnly(2025, 11, 30))));
    }

    // --- Committed: the engaged figure trades are validated against ---

    [Fact]
    public void Engaged_IsTheRosterAfterEveryAcceptedTradeHasLanded()
    {
        var committed = RosterWindow.Committed().Compile();

        Assert.True(committed(Held()));
        // The mirror image of OwnedOn: this is what the team will be, not what
        // it is. Validating a trade against today's figure is what let a GM
        // accept a $9M contract in the morning and bust the cap in the
        // afternoon, each trade looking fine on its own.
        Assert.False(committed(Leaving()));
        Assert.True(committed(Arriving()));
        Assert.False(committed(Departed()));
    }

    [Fact]
    public void EngagedAndToday_DisagreeOnExactlyTheTwoSpotsATradeCreates()
    {
        // The whole reason both predicates exist. On every other spot they
        // agree, which is why one filter answered both questions for a year.
        var today = RosterWindow.OwnedOn(Wednesday).Compile();
        var committed = RosterWindow.Committed().Compile();

        Assert.Equal(today(Held()), committed(Held()));
        Assert.Equal(today(Departed()), committed(Departed()));

        Assert.True(today(Leaving()));
        Assert.False(committed(Leaving()));
        Assert.False(today(Arriving()));
        Assert.True(committed(Arriving()));
    }

    // --- The two screen flags ---

    [Fact]
    public void EngagedFlag_IsMineNowAndPromisedAway()
    {
        var engaged = RosterWindow.Engaged(Wednesday).Compile();

        Assert.True(engaged(Leaving()));
        Assert.False(engaged(Held()));
        // Never both marks at once: a man who has not arrived cannot be shown
        // as leaving.
        Assert.False(engaged(Arriving()));
        Assert.False(engaged(Departed()));
    }

    [Fact]
    public void ArrivingFlag_IsOnlyTheManWhoseStintHasNotOpened()
    {
        var arriving = RosterWindow.Arriving(Wednesday).Compile();

        Assert.True(arriving(Arriving()));
        Assert.False(arriving(Held()));
        Assert.False(arriving(Leaving()));
    }
}
