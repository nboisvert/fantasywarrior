using FantasyWarrior.Core.Cockman;

namespace FantasyWarrior.Core.Tests.Cockman;

public class CampaignSelectionTests
{
    private static readonly DateTime Now = new(2026, 8, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SelectNext_NullWhenNoCandidates()
    {
        Assert.Null(CampaignSelection.SelectNext([], [], Now));
    }

    [Fact]
    public void SelectNext_PicksAnActiveUnseenCampaign()
    {
        var candidates = new[] { new CampaignCandidate(1, Now.AddDays(-1), null) };
        Assert.Equal(1, CampaignSelection.SelectNext(candidates, [], Now));
    }

    [Fact]
    public void SelectNext_ExcludesAlreadySeen()
    {
        var candidates = new[] { new CampaignCandidate(1, Now.AddDays(-1), null) };
        Assert.Null(CampaignSelection.SelectNext(candidates, [1], Now));
    }

    [Fact]
    public void SelectNext_ExcludesNotYetStarted()
    {
        var candidates = new[] { new CampaignCandidate(1, Now.AddDays(1), null) };
        Assert.Null(CampaignSelection.SelectNext(candidates, [], Now));
    }

    [Fact]
    public void SelectNext_ExcludesExpired()
    {
        // This is what stops a brand-new user from being shown a backlog of
        // every past campaign — an ended one is simply never eligible again.
        var candidates = new[] { new CampaignCandidate(1, Now.AddDays(-10), Now.AddDays(-1)) };
        Assert.Null(CampaignSelection.SelectNext(candidates, [], Now));
    }

    [Fact]
    public void SelectNext_EvergreenCampaignStaysEligibleArbitrarilyFarPastStart()
    {
        var candidates = new[] { new CampaignCandidate(1, Now.AddYears(-1), null) };
        Assert.Equal(1, CampaignSelection.SelectNext(candidates, [], Now));
    }

    [Fact]
    public void SelectNext_TwoActiveUnseen_EarliestStartWins()
    {
        var candidates = new[]
        {
            new CampaignCandidate(2, Now.AddDays(-1), null),
            new CampaignCandidate(1, Now.AddDays(-5), null),
        };
        Assert.Equal(1, CampaignSelection.SelectNext(candidates, [], Now));
    }

    [Fact]
    public void SelectNext_TiedStart_LowestIdWins()
    {
        var candidates = new[]
        {
            new CampaignCandidate(5, Now.AddDays(-1), null),
            new CampaignCandidate(2, Now.AddDays(-1), null),
        };
        Assert.Equal(2, CampaignSelection.SelectNext(candidates, [], Now));
    }
}
