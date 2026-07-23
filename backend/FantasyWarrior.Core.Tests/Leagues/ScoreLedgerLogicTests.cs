using FantasyWarrior.Core.Leagues;

namespace FantasyWarrior.Core.Tests.Leagues;

public class ScoreLedgerLogicTests
{
    [Fact]
    public void ShouldRecord_FalseWhenNothingChanged()
    {
        Assert.False(ScoreLedgerLogic.ShouldRecord(42, 42, countedBefore: true, countedAfter: true));
        Assert.False(ScoreLedgerLogic.ShouldRecord(0, 0, countedBefore: false, countedAfter: false));
    }

    [Fact]
    public void ShouldRecord_TrueWhenPointsChanged_CountedStatusUnchanged()
    {
        // Player played a game but stayed comfortably in/out of the top-X.
        Assert.True(ScoreLedgerLogic.ShouldRecord(42, 45, countedBefore: true, countedAfter: true));
    }

    [Fact]
    public void ShouldRecord_TrueWhenCountedFlagFlips_EvenIfPointsSomehowDidnt()
    {
        // Boundary-crossing case: another player's change could flip this
        // player's counted status without his own points moving.
        Assert.True(ScoreLedgerLogic.ShouldRecord(32, 32, countedBefore: false, countedAfter: true));
        Assert.True(ScoreLedgerLogic.ShouldRecord(35, 35, countedBefore: true, countedAfter: false));
    }

    [Fact]
    public void ShouldRecord_TrueOnFirstRunBaseline()
    {
        // No prior state (before = 0, not counted) but the player already has
        // points and/or counts today -> baseline entry gets logged.
        Assert.True(ScoreLedgerLogic.ShouldRecord(0, 10, countedBefore: false, countedAfter: true));
    }

    [Fact]
    public void ShouldRecord_TrueWhenBothChangeAtOnce()
    {
        // The classic scenario Nick described: a big night moves both the
        // points and the counted flag in the same run.
        Assert.True(ScoreLedgerLogic.ShouldRecord(32, 36, countedBefore: false, countedAfter: true));
    }
}
