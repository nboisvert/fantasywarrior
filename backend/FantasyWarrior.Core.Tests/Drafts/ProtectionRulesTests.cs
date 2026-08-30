using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;

namespace FantasyWarrior.Core.Tests.Drafts;

public class ProtectionRulesTests
{
    /// <summary>Les Mordus' bars, which are also the defaults every league starts from.</summary>
    private static readonly AutoProtectConfig Auto = new();

    // --- skaters: the bar is 100, inclusive ---

    [Theory]
    [InlineData("F")]
    [InlineData("D")]
    public void Skater_IsAutoProtected_UpToAndIncludingTheThreshold(string group)
    {
        Assert.True(ProtectionRules.IsAutoProtected(group, 99, Auto));
        Assert.True(ProtectionRules.IsAutoProtected(group, 100, Auto));
        Assert.False(ProtectionRules.IsAutoProtected(group, 101, Auto));
    }

    // --- goalies: the bar is 50, inclusive ---

    [Fact]
    public void Goalie_IsAutoProtected_UpToAndIncludingTheThreshold()
    {
        Assert.True(ProtectionRules.IsAutoProtected("G", 49, Auto));
        Assert.True(ProtectionRules.IsAutoProtected("G", 50, Auto));
        Assert.False(ProtectionRules.IsAutoProtected("G", 51, Auto));
    }

    /// <summary>
    /// The whole point of two thresholds: at 75 games a skater is still a
    /// prospect and a goalie is an established starter. One bar for both would
    /// keep goalies untouchable for roughly twice as many seasons.
    /// </summary>
    [Fact]
    public void AtSeventyFiveGames_TheSkaterIsProtectedAndTheGoalieIsNot()
    {
        Assert.True(ProtectionRules.IsAutoProtected("F", 75, Auto));
        Assert.False(ProtectionRules.IsAutoProtected("G", 75, Auto));
    }

    // --- the ends ---

    [Theory]
    [InlineData("F")]
    [InlineData("D")]
    [InlineData("G")]
    public void APlayerWhoHasNeverPlayedIsAutoProtected(string group)
    {
        Assert.True(ProtectionRules.IsAutoProtected(group, 0, Auto));
    }

    [Fact]
    public void AVeteranIsNot()
    {
        Assert.False(ProtectionRules.IsAutoProtected("F", 1300, Auto));
    }

    /// <summary>
    /// The Équipe slot holds a franchise, not a player. It can only ever move
    /// against another franchise, so the draft cannot take it and this rule must
    /// not claim to be what protects it.
    /// </summary>
    [Fact]
    public void AFranchiseIsNeverAutoProtected()
    {
        Assert.False(ProtectionRules.IsAutoProtected("T", 0, Auto));
        Assert.False(ProtectionRules.IsAutoProtected("T", 1300, Auto));
    }

    // --- which shelter, for the Protections screen ---

    [Fact]
    public void KindOf_AVeteranNobodyProtectedIsExposed()
    {
        Assert.Equal(ProtectionKind.Exposed, ProtectionRules.KindOf("F", 400, protectedByGm: false, Auto));
    }

    [Fact]
    public void KindOf_ASlotBeatsEverythingElse()
    {
        Assert.Equal(ProtectionKind.ByGm, ProtectionRules.KindOf("F", 400, protectedByGm: true, Auto));
    }

    /// <summary>
    /// A GM can waste a slot on someone already safe. The screen must say so
    /// rather than tidy it away — that wasted slot is the whole reason the
    /// protection screen is worth building.
    /// </summary>
    [Fact]
    public void KindOf_ASlotSpentOnAnAlreadySafeProspectStillReadsAsHisChoice()
    {
        Assert.Equal(ProtectionKind.ByGm, ProtectionRules.KindOf("F", 10, protectedByGm: true, Auto));
    }

    [Fact]
    public void KindOf_TooFewGamesIsFreeAndNobodyChoseIt()
    {
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("F", 100, protectedByGm: false, Auto));
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("G", 50, protectedByGm: false, Auto));
    }

    /// <summary>
    /// Unknown is not Auto. He is equally untouchable, but saying "auto-protected"
    /// would report a hole in our sync as a rule of the pool.
    /// </summary>
    [Fact]
    public void KindOf_NeverSyncedIsItsOwnAnswer()
    {
        Assert.Equal(ProtectionKind.Unknown, ProtectionRules.KindOf("F", null, protectedByGm: false, Auto));
    }

    [Fact]
    public void KindOf_AFranchiseIsOutOfReachWithoutSpendingAnything()
    {
        Assert.Equal(ProtectionKind.Auto, ProtectionRules.KindOf("T", null, protectedByGm: false, Auto));
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

        var takeable = DraftPool.IsEligible(candidate, DraftSegment.Steal, pickingTeamId: 1, maxLossesPerTeam: 2, Auto);
        var kind = ProtectionRules.KindOf(group, career, protectedByGm, Auto);

        Assert.Equal(takeable, kind == ProtectionKind.Exposed);
    }
}
