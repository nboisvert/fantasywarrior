using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftOrderTests
{
    // Three teams, worst first once reversed: 30 -> 31, 20 -> 32, 10 -> 33.
    private static readonly int[] Order = [33, 32, 31];

    private static PickSlot Pick(int id, int round, int inRound, int team, bool used = false) =>
        new(id, round, inRound, team, used);

    // Two rookie rounds for three teams, in the same order as Order.
    private static List<PickSlot> Picks() =>
    [
        Pick(1, 1, 1, 33), Pick(2, 1, 2, 32), Pick(3, 1, 3, 31),
        Pick(4, 2, 1, 33), Pick(5, 2, 2, 32), Pick(6, 2, 3, 31),
    ];

    // --- reverse standings ---

    [Fact]
    public void ReverseStandings_PutsTheWorstTeamFirst()
    {
        var order = DraftOrder.ReverseStandings([(31, 30.0), (32, 20.0), (33, 10.0)]);
        Assert.Equal([33, 32, 31], order);
    }

    [Fact]
    public void ReverseStandings_BreaksTiesOnTeamId()
    {
        // Without this the order would depend on whatever order SQL returned
        // the rows in, and two reads of the same standings could disagree.
        var order = DraftOrder.ReverseStandings([(9, 50.0), (4, 50.0), (7, 50.0)]);
        Assert.Equal([4, 7, 9], order);
    }

    [Fact]
    public void ReverseStandings_EmptyInEmptyOut()
    {
        Assert.Empty(DraftOrder.ReverseStandings([]));
    }

    // --- the steal segment ---

    [Fact]
    public void StealTurn_FirstTurnGoesToTheWorstTeam()
    {
        var turn = DraftOrder.StealTurn(Order, stealRounds: 2, selectionsMade: 0);

        Assert.NotNull(turn);
        Assert.Equal(DraftSegment.Steal, turn!.Segment);
        Assert.Equal(33, turn.TeamId);
        Assert.Equal(1, turn.Round);
        Assert.Equal(1, turn.PickInRound);
        Assert.Null(turn.DraftPickId);
    }

    [Fact]
    public void StealTurn_IsLinearNotASnake()
    {
        // THE regression test for the ordering rule (Nick, 2026-08-25). Turn 3
        // opens round 2 and must go back to the worst team, not stay with the
        // best one as a snake would.
        var openingRound2 = DraftOrder.StealTurn(Order, stealRounds: 2, selectionsMade: 3);

        Assert.NotNull(openingRound2);
        Assert.Equal(33, openingRound2!.TeamId);
        Assert.Equal(2, openingRound2.Round);
        Assert.Equal(1, openingRound2.PickInRound);
    }

    [Fact]
    public void StealTurn_LastTurnOfTheSegment()
    {
        var turn = DraftOrder.StealTurn(Order, stealRounds: 2, selectionsMade: 5);

        Assert.NotNull(turn);
        Assert.Equal(31, turn!.TeamId);
        Assert.Equal(2, turn.Round);
    }

    [Fact]
    public void StealTurn_NullOnceTheSegmentIsOver()
    {
        Assert.Null(DraftOrder.StealTurn(Order, stealRounds: 2, selectionsMade: 6));
    }

    // --- the rookie segment ---

    [Fact]
    public void RookieTurn_TakesTheLowestUnusedEntitlement()
    {
        var turn = DraftOrder.RookieTurn(Picks(), stealTurnCount: 6, selectionsMade: 6);

        Assert.NotNull(turn);
        Assert.Equal(DraftSegment.Rookie, turn!.Segment);
        Assert.Equal(1, turn.DraftPickId);
        Assert.Equal(33, turn.TeamId);
    }

    [Fact]
    public void RookieTurn_FollowsTheCurrentHolderNotTheOriginalOne()
    {
        // Team 31 traded its first-rounder to team 32. The order is untouched —
        // the pick still goes third — but the picker changed. This is why the
        // two segments cannot share one ordering rule.
        var picks = Picks();
        picks[2] = Pick(3, 1, 3, team: 32);

        var turn = DraftOrder.RookieTurn(picks.Where(p => p.DraftPickId == 3), 6, 8);

        Assert.Equal(32, turn!.TeamId);
        Assert.Equal(3, turn.PickInRound);
    }

    [Fact]
    public void RookieTurn_SkipsSpentEntitlements()
    {
        var picks = Picks();
        picks[0] = Pick(1, 1, 1, 33, used: true);

        var turn = DraftOrder.RookieTurn(picks, stealTurnCount: 6, selectionsMade: 7);

        Assert.Equal(2, turn!.DraftPickId);
        Assert.Equal(32, turn.TeamId);
    }

    [Fact]
    public void RookieTurn_NullWhenEveryEntitlementIsSpent()
    {
        var spent = Picks().Select(p => p with { Used = true });
        Assert.Null(DraftOrder.RookieTurn(spent, stealTurnCount: 6, selectionsMade: 12));
    }

    // --- the boundary between them ---

    [Fact]
    public void OnTheClock_CrossesFromStealToRookie()
    {
        var last = DraftOrder.OnTheClock(Order, 2, Picks(), selectionsMade: 5);
        var first = DraftOrder.OnTheClock(Order, 2, Picks(), selectionsMade: 6);

        Assert.Equal(DraftSegment.Steal, last!.Segment);
        Assert.Equal(DraftSegment.Rookie, first!.Segment);
        // The index stays continuous across the seam — it is the turn's
        // identity, and the unique index that guards it does not know about
        // segments.
        Assert.Equal(6, first.OverallIndex);
    }

    [Fact]
    public void OnTheClock_ZeroStealRoundsStartsInTheRookieSegment()
    {
        var turn = DraftOrder.OnTheClock(Order, stealRounds: 0, Picks(), selectionsMade: 0);

        Assert.Equal(DraftSegment.Rookie, turn!.Segment);
        Assert.Equal(1, turn.DraftPickId);
    }

    [Fact]
    public void OnTheClock_NullWhenTheDraftIsFinished()
    {
        var spent = Picks().Select(p => p with { Used = true }).ToList();
        Assert.Null(DraftOrder.OnTheClock(Order, 2, spent, selectionsMade: 12));
    }

    // --- "you pick in N" ---

    [Fact]
    public void TurnsUntil_IsZeroForTheTeamOnTheClock()
    {
        Assert.Equal(0, DraftOrder.TurnsUntil(Order, 2, Picks(), selectionsMade: 0, teamId: 33));
    }

    [Fact]
    public void TurnsUntil_CountsForwardToTheNextTurn()
    {
        Assert.Equal(2, DraftOrder.TurnsUntil(Order, 2, Picks(), selectionsMade: 0, teamId: 31));
    }

    [Fact]
    public void TurnsUntil_CrossesTheSegmentBoundary()
    {
        // Five steal turns are gone; team 33's next turn is the first rookie
        // pick, one turn after the steal segment's last.
        Assert.Equal(1, DraftOrder.TurnsUntil(Order, 2, Picks(), selectionsMade: 5, teamId: 33));
    }

    [Fact]
    public void TurnsUntil_NullWhenTheTeamHasNoTurnLeft()
    {
        var noPicks = new List<PickSlot>();
        Assert.Null(DraftOrder.TurnsUntil(Order, 2, noPicks, selectionsMade: 6, teamId: 33));
    }
}
