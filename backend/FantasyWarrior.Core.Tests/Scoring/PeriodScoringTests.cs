using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Scoring;

public class PeriodScoringTests
{
    // --- ApplyFinalize: the guard that keeps standings from silently doubling ---

    [Fact]
    public void ApplyFinalize_BanksAPeriodNotYetBanked()
    {
        var result = PeriodScoring.ApplyFinalize(
            finalizedThroughPeriodIndex: 3, periodIndex: 4, finalizedPoints: 120, periodPoints: 18);

        Assert.Equal(new FinalizeResult(4, 138), result);
    }

    [Fact]
    public void ApplyFinalize_IsANoOpWhenThePeriodIsAlreadyBanked()
    {
        // A retried GitHub Actions run, a manual catch-up, a crash between two
        // writes -- all of these re-enter here, and none may add the points
        // twice.
        Assert.Null(PeriodScoring.ApplyFinalize(4, 4, 138, 18));
    }

    [Fact]
    public void ApplyFinalize_RefusesToGoBackwards()
    {
        Assert.Null(PeriodScoring.ApplyFinalize(finalizedThroughPeriodIndex: 6, periodIndex: 4, 200, 18));
    }

    [Fact]
    public void ApplyFinalize_RunningItTwiceGivesTheSameTotalAsOnce()
    {
        var first = PeriodScoring.ApplyFinalize(3, 4, 120, 18);
        Assert.NotNull(first);

        var second = PeriodScoring.ApplyFinalize(first!.Value.FinalizedThroughPeriodIndex, 4, first.Value.FinalizedPoints, 18);

        Assert.Null(second);
        Assert.Equal(138, first.Value.FinalizedPoints);
    }

    [Fact]
    public void ApplyFinalize_BanksTheFirstPeriodOfASeason()
    {
        var result = PeriodScoring.ApplyFinalize(0, 1, 0, 22);

        Assert.Equal(new FinalizeResult(1, 22), result);
    }

    [Fact]
    public void ApplyFinalize_CanCatchUpSeveralPeriodsInOrder()
    {
        // A missed cron leaves two weeks unbanked; replaying them in order
        // must land on the same total as if nothing had been missed.
        var state = new FinalizeResult(2, 100);
        foreach (var (index, points) in new[] { (3, 10.0), (4, 20.0) })
            state = PeriodScoring.ApplyFinalize(state.FinalizedThroughPeriodIndex, index, state.FinalizedPoints, points)!.Value;

        Assert.Equal(new FinalizeResult(4, 130), state);
    }

    [Fact]
    public void ApplyFinalize_BanksZeroForABreakWeek()
    {
        // The Olympic break weeks have no games; they still have to advance the
        // index, or every later period would be blocked behind them.
        var result = PeriodScoring.ApplyFinalize(18, 19, 400, 0);

        Assert.Equal(new FinalizeResult(19, 400), result);
    }

    // --- ShouldFinalize: the grace day for late NHL corrections ---

    [Fact]
    public void ShouldFinalize_WaitsUntilAfterTheGraceDay()
    {
        // The NHL amends boxscores after the fact; banking the night the week
        // ends would freeze whatever was known then and never revisit it.
        Assert.False(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), lastStatDate: DateOnly.Parse("2026-01-11")));
        Assert.True(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), lastStatDate: DateOnly.Parse("2026-01-12")));
    }

    [Fact]
    public void ShouldFinalize_IsTrueLongAfterTheFact()
    {
        Assert.True(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), DateOnly.Parse("2026-03-01")));
    }

    [Fact]
    public void ShouldFinalize_IsFalseMidWeek()
    {
        Assert.False(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), DateOnly.Parse("2026-01-08")));
    }

    [Fact]
    public void ShouldFinalize_HonoursALongerGracePeriod()
    {
        Assert.False(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), DateOnly.Parse("2026-01-12"), graceDays: 3));
        Assert.True(PeriodScoring.ShouldFinalize(DateOnly.Parse("2026-01-11"), DateOnly.Parse("2026-01-14"), graceDays: 3));
    }

    // --- LiveScore / NeedsWrite ---

    [Fact]
    public void LiveScore_IsBankedPlusTheCurrentPeriod()
    {
        Assert.Equal(138, PeriodScoring.LiveScore(finalizedScore: 120, currentPeriodPoints: 18));
    }

    [Fact]
    public void LiveScore_RecomputingTheCurrentPeriodNeverAccumulates()
    {
        // The current period is always replaced, never added to -- this is what
        // makes the whole nightly job safe to re-run.
        Assert.Equal(130, PeriodScoring.LiveScore(120, 10));
        Assert.Equal(138, PeriodScoring.LiveScore(120, 18));
    }

    [Fact]
    public void NeedsWrite_IsFalseWhenNothingMoved()
    {
        // Most players have no game on most nights; skipping those writes is
        // what keeps the nightly job cheap.
        Assert.False(PeriodScoring.NeedsWrite(12, 3, 12, 3));
    }

    [Fact]
    public void NeedsWrite_IsTrueWhenPointsOrGamesChange()
    {
        Assert.True(PeriodScoring.NeedsWrite(12, 3, 14, 3));
        Assert.True(PeriodScoring.NeedsWrite(12, 3, 12, 4));
    }

    [Fact]
    public void NeedsWrite_IsTrueForAScorelessGame()
    {
        // A player who played but did not produce still changes gamesPlayed,
        // which the points-per-game denominator depends on.
        Assert.True(PeriodScoring.NeedsWrite(0, 2, 0, 3));
    }
}
