using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class ProtectionRulesTests
{
    // --- skaters: the bar is 100, inclusive ---

    [Theory]
    [InlineData("F")]
    [InlineData("D")]
    public void Skater_IsAutoProtected_UpToAndIncludingTheThreshold(string group)
    {
        Assert.True(ProtectionRules.IsAutoProtected(group, 99));
        Assert.True(ProtectionRules.IsAutoProtected(group, 100));
        Assert.False(ProtectionRules.IsAutoProtected(group, 101));
    }

    // --- goalies: the bar is 50, inclusive ---

    [Fact]
    public void Goalie_IsAutoProtected_UpToAndIncludingTheThreshold()
    {
        Assert.True(ProtectionRules.IsAutoProtected("G", 49));
        Assert.True(ProtectionRules.IsAutoProtected("G", 50));
        Assert.False(ProtectionRules.IsAutoProtected("G", 51));
    }

    /// <summary>
    /// The whole point of two thresholds: at 75 games a skater is still a
    /// prospect and a goalie is an established starter. One bar for both would
    /// keep goalies untouchable for roughly twice as many seasons.
    /// </summary>
    [Fact]
    public void AtSeventyFiveGames_TheSkaterIsProtectedAndTheGoalieIsNot()
    {
        Assert.True(ProtectionRules.IsAutoProtected("F", 75));
        Assert.False(ProtectionRules.IsAutoProtected("G", 75));
    }

    // --- the ends ---

    [Theory]
    [InlineData("F")]
    [InlineData("D")]
    [InlineData("G")]
    public void APlayerWhoHasNeverPlayedIsAutoProtected(string group)
    {
        Assert.True(ProtectionRules.IsAutoProtected(group, 0));
    }

    [Fact]
    public void AVeteranIsNot()
    {
        Assert.False(ProtectionRules.IsAutoProtected("F", 1300));
    }

    /// <summary>
    /// The Équipe slot holds a franchise, not a player. It can only ever move
    /// against another franchise, so the draft cannot take it and this rule must
    /// not claim to be what protects it.
    /// </summary>
    [Fact]
    public void AFranchiseIsNeverAutoProtected()
    {
        Assert.False(ProtectionRules.IsAutoProtected("T", 0));
        Assert.False(ProtectionRules.IsAutoProtected("T", 1300));
    }
}
