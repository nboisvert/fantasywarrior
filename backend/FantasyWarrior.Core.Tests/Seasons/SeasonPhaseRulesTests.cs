using FantasyWarrior.Core.Seasons;

namespace FantasyWarrior.Core.Tests.Seasons;

public class SeasonPhaseRulesTests
{
    [Theory]
    [InlineData(LeagueSeasonPhase.Preparing, LeagueSeasonPhase.Protecting)]
    [InlineData(LeagueSeasonPhase.Protecting, LeagueSeasonPhase.Drafting)]
    [InlineData(LeagueSeasonPhase.Drafting, LeagueSeasonPhase.PreSeason)]
    [InlineData(LeagueSeasonPhase.PreSeason, LeagueSeasonPhase.InSeason)]
    [InlineData(LeagueSeasonPhase.InSeason, LeagueSeasonPhase.Complete)]
    public void Next_WalksTheLifecycleOneStepAtATime(LeagueSeasonPhase from, LeagueSeasonPhase expected)
    {
        Assert.Equal(expected, SeasonPhaseRules.Next(from));
    }

    [Fact]
    public void Next_HasNoStepPastComplete()
    {
        Assert.Null(SeasonPhaseRules.Next(LeagueSeasonPhase.Complete));
    }

    [Fact]
    public void CanTransition_RefusesSkippingAPhase()
    {
        // Protecting straight to PreSeason would mean a draft that never happened.
        Assert.False(SeasonPhaseRules.CanTransition(LeagueSeasonPhase.Protecting, LeagueSeasonPhase.PreSeason));
    }

    [Fact]
    public void CanTransition_RefusesGoingBackward()
    {
        Assert.False(SeasonPhaseRules.CanTransition(LeagueSeasonPhase.Drafting, LeagueSeasonPhase.Protecting));
    }

    [Fact]
    public void CanTransition_AcceptsTheOneLegalStep()
    {
        Assert.True(SeasonPhaseRules.CanTransition(LeagueSeasonPhase.PreSeason, LeagueSeasonPhase.InSeason));
    }

    [Theory]
    [InlineData(LeagueSeasonPhase.Protecting, false)]
    [InlineData(LeagueSeasonPhase.Drafting, false)]
    [InlineData(LeagueSeasonPhase.Preparing, true)]
    [InlineData(LeagueSeasonPhase.PreSeason, true)]
    [InlineData(LeagueSeasonPhase.InSeason, true)]
    [InlineData(LeagueSeasonPhase.Complete, true)]
    public void CanTrade_IsFalseOnlyDuringProtectingAndDrafting(LeagueSeasonPhase phase, bool expected)
    {
        Assert.Equal(expected, SeasonPhaseRules.CanTrade(phase));
    }
}
