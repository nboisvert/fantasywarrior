using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftRulesTests
{
    private const long Cap = 134_000_000;
    private const long Default = 1_000_000;

    private static IReadOnlyList<string> Select(
        long capBefore = 100_000_000, int countBefore = 25,
        long? incoming = 5_000_000, long? cap = Cap) =>
        DraftRules.ValidateSelection("Boisvert", capBefore, countBefore, incoming, Default, cap);

    [Fact]
    public void Selection_UnderTheCapIsFine()
    {
        Assert.Empty(Select());
    }

    [Fact]
    public void Selection_OverTheCapIsRefusedAndTheTeamIsNamed()
    {
        var errors = Select(capBefore: 133_000_000, incoming: 2_000_000);

        var error = Assert.Single(errors);
        Assert.Contains("Boisvert", error);
        Assert.Contains("over", error);
    }

    [Fact]
    public void Selection_OverTheRosterMaximumIsNOTRefused()
    {
        // The max joined the min here (Nick, 2026-08-29). A draft pick only
        // ever runs while the season is Drafting, never InSeason, and
        // PreSeason exists precisely so a roster coming out of the draft can be
        // out of bounds and still have a window to trade itself back into
        // shape. Without this, a team already sitting at RosterMax before its
        // steal turn -- a real state, not a hypothetical one -- could never
        // take anyone.
        Assert.Empty(Select(countBefore: 35));
    }

    [Fact]
    public void Selection_UnderTheRosterMinimumIsNOTRefused()
    {
        // THE regression test for the trap. season-lifecycle.md section 5:
        // PreSeason exists precisely because a team can come out of the draft
        // under RosterMin - two players lost, one drafted back. Enforcing the
        // minimum here would make that window unreachable. A minimum cannot be
        // breached by an add anyway; the point is that nothing must ever pass
        // one in.
        Assert.Empty(Select(countBefore: 21));
    }

    [Fact]
    public void Selection_AnUnknownContractIsChargedTheLeagueDefault()
    {
        // Not zero - the same rule vStandings applies, or the before and after
        // would be on two different scales.
        var errors = Select(capBefore: Cap, incoming: null);

        var error = Assert.Single(errors);
        Assert.Contains("over", error);
    }

    [Fact]
    public void Selection_NullCapMeansNoRuleNotZero()
    {
        Assert.Empty(Select(capBefore: 900_000_000, countBefore: 99, cap: null));
    }

    [Fact]
    public void Loss_BelowTheCapIsAllowed()
    {
        Assert.Empty(DraftRules.ValidateLoss("Lachance", victimLossesSoFar: 1, maxLossesPerTeam: 2));
    }

    [Fact]
    public void Loss_AtTheCapIsRefusedAndTheTeamIsNamed()
    {
        var error = Assert.Single(
            DraftRules.ValidateLoss("Lachance", victimLossesSoFar: 2, maxLossesPerTeam: 2));

        Assert.Contains("Lachance", error);
        Assert.Contains("2", error);
    }

    [Fact]
    public void Loss_NoCapMeansNoRule()
    {
        Assert.Empty(DraftRules.ValidateLoss("Lachance", victimLossesSoFar: 9, maxLossesPerTeam: null));
    }
}
