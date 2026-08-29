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

    // --- which shelter, for the Protections screen ---

    [Fact]
    public void KindOf_AVeteranNobodyProtectedIsExposed()
    {
        Assert.Equal(ProtectionKind.Exposed, ProtectionRules.KindOf("F", 400, protectedByGm: false));
    }

    [Fact]
    public void KindOf_ASlotBeatsEverythingElse()
    {
        Assert.Equal(ProtectionKind.ByGm, ProtectionRules.KindOf("F", 400, protectedByGm: true));
    }

    /// <summary>
    /// A GM can waste a slot on someone already safe. The screen must say so
    /// rather than tidy it away — that wasted slot is the whole reason the
    /// protection screen is worth building.
    /// </summary>
    [Fact]
    public void KindOf_ASlotSpentOnAnAlreadySafeProspectStillReadsAsHisChoice()
    {
        Assert.Equal(ProtectionKind.ByGm, ProtectionRules.KindOf("F", 10, protectedByGm: true));
    }

    [Fact]
    public void KindOf_TooFewGamesIsFreeAndNobodyChoseIt()
    {
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("F", 100, protectedByGm: false));
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("G", 50, protectedByGm: false));
    }

    /// <summary>
    /// Unknown is not Auto. He is equally untouchable, but saying "auto-protected"
    /// would report a hole in our sync as a rule of the pool.
    /// </summary>
    [Fact]
    public void KindOf_NeverSyncedIsItsOwnAnswer()
    {
        Assert.Equal(ProtectionKind.Unknown, ProtectionRules.KindOf("F", null, protectedByGm: false));
    }

    [Fact]
    public void KindOf_AFranchiseIsOutOfReachWithoutSpendingAnything()
    {
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("T", null, protectedByGm: false));
    }

    /// <summary>
    /// The load-bearing test. A screen that said "exposed" about a man the steal
    /// pool then refused to hand over would be worse than no screen, so the two
    /// rules are asserted against each other across the whole grid.
    /// </summary>
    [Theory]
    [InlineData("F", 400, false)]
    [InlineData("F", 400, true)]
    [InlineData("F", 100, false)]
    [InlineData("F", 101, false)]
    [InlineData("G", 50, false)]
    [InlineData("G", 51, false)]
    [InlineData("D", null, false)]
    [InlineData("D", 0, false)]
    public void KindOf_AgreesWithTheStealPool(string group, int? career, bool protectedByGm)
    {
        var candidate = new DraftCandidate(
            PlayerId: 1, PositionGroup: group, CareerNhlGames: career,
            OwnerTeamId: 2, ProtectedByGm: protectedByGm, OwnerLossesSoFar: 0);

        var takeable = DraftPool.IsEligible(candidate, DraftSegment.Steal, pickingTeamId: 1, maxLossesPerTeam: 2);
        var kind = ProtectionRules.KindOf(group, career, protectedByGm);

        Assert.Equal(takeable, kind == ProtectionKind.Exposed);
    }
}
