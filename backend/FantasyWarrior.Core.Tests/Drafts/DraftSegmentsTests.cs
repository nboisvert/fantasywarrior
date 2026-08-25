using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftSegmentsTests
{
    // The real shape of Les Mordus: 14 teams, 2 steal rounds.
    private const int Teams = 14;

    [Fact]
    public void StealTurnCount_IsTeamsTimesRounds()
    {
        Assert.Equal(28, DraftSegments.StealTurnCount(Teams, 2));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(14, 0)]
    [InlineData(14, -1)]
    public void StealTurnCount_IsZeroWhenThereIsNoSegment(int teams, int rounds)
    {
        Assert.Equal(0, DraftSegments.StealTurnCount(teams, rounds));
    }

    [Fact]
    public void SegmentOf_SwitchesExactlyAtTheBoundary()
    {
        Assert.Equal(DraftSegment.Steal, DraftSegments.SegmentOf(27, 28));
        Assert.Equal(DraftSegment.Rookie, DraftSegments.SegmentOf(28, 28));
    }

    [Fact]
    public void SegmentOf_IsAllRookieWhenThereAreNoStealRounds()
    {
        Assert.Equal(DraftSegment.Rookie, DraftSegments.SegmentOf(0, 0));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(14, 2)]
    [InlineData(27, 2)]
    public void StealRound_CountsFromOne(int overallIndex, int expected)
    {
        Assert.Equal(expected, DraftSegments.StealRound(overallIndex, Teams));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 14)]
    [InlineData(14, 1)]
    public void StealPickInRound_RestartsEachRound(int overallIndex, int expected)
    {
        // Restarting rather than reversing is what makes the order linear.
        Assert.Equal(expected, DraftSegments.StealPickInRound(overallIndex, Teams));
    }
}
