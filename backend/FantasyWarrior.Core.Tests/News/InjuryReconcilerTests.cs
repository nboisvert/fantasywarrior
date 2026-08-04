using FantasyWarrior.Data.Entities;
using FantasyWarrior.Jobs.News;

namespace FantasyWarrior.Core.Tests.News;

/// <summary>
/// The rules that decide who is newly hurt, who changed, and who is over.
///
/// Worth testing on its own because its mistakes are silent: a row left open
/// marks a healthy player out for the rest of the season and nothing errors,
/// and a row closed too eagerly makes an injury vanish from every screen at
/// once.
/// </summary>
public class InjuryReconcilerTests
{
    private static OpenInjury Open(int id, long playerId, string? type = "Knee", string? status = null) =>
        new(id, playerId, status ?? InjuryStatuses.Injured, type);

    private static ListedInjury Listed(long playerId, string? type = "Knee", string? status = null) =>
        new(playerId, status ?? InjuryStatuses.Injured, type);

    [Fact]
    public void APlayerListedForTheFirstTime_OpensARow()
    {
        var plan = InjuryReconciler.Plan([], [Listed(8471675)]);
        Assert.Equal(8471675, Assert.Single(plan.ToOpen).PlayerId);
        Assert.Empty(plan.ToResolve);
        Assert.Empty(plan.ToRelabel);
    }

    [Fact]
    public void APlayerStillListedWithTheSameInjury_IsLeftAlone()
    {
        var plan = InjuryReconciler.Plan([Open(1, 8471675)], [Listed(8471675)]);
        Assert.Empty(plan.ToOpen);
        Assert.Empty(plan.ToRelabel);
        Assert.Empty(plan.ToResolve);
    }

    /// <summary>The whole point of the source being a state page: he is gone
    /// from it, so he is cleared. Nobody publishes "healed".</summary>
    [Fact]
    public void APlayerNoLongerListed_IsResolved()
    {
        var plan = InjuryReconciler.Plan([Open(7, 8471675)], []);
        Assert.Equal(7, Assert.Single(plan.ToResolve));
        Assert.Empty(plan.ToOpen);
    }

    /// <summary>Same spell, better wording — the row survives so ReportedUtc
    /// keeps answering "hurt since when".</summary>
    [Fact]
    public void ARewordedInjury_RelabelsTheRowInsteadOfRestartingIt()
    {
        var plan = InjuryReconciler.Plan([Open(7, 8471675, "Knee")], [Listed(8471675, "Lower Body")]);
        Assert.Equal((7, "Lower Body"), Assert.Single(plan.ToRelabel));
        Assert.Empty(plan.ToResolve);
        Assert.Empty(plan.ToOpen);
    }

    /// <summary>Injured and suspended are different events about the same man,
    /// so the dates must not be merged into one row.</summary>
    [Fact]
    public void AChangeOfStatus_ClosesTheOldSpellAndOpensANewOne()
    {
        var plan = InjuryReconciler.Plan(
            [Open(7, 8471675, "Knee")],
            [Listed(8471675, "Suspension", InjuryStatuses.Suspended)]);

        Assert.Equal(7, Assert.Single(plan.ToResolve));
        var opened = Assert.Single(plan.ToOpen);
        Assert.Equal(InjuryStatuses.Suspended, opened.Status);
        Assert.Equal("Suspension", opened.InjuryType);
        Assert.Empty(plan.ToRelabel);
    }

    [Fact]
    public void APlayerListedTwiceOnOnePage_IsNotTwoInjuries()
    {
        var plan = InjuryReconciler.Plan([], [Listed(8471675, "Knee"), Listed(8471675, "Ankle")]);
        Assert.Equal("Knee", Assert.Single(plan.ToOpen).InjuryType);
    }

    /// <summary>Not something this job creates, but a mess an earlier run could
    /// have left; carrying it forward would double-count him forever.</summary>
    [Fact]
    public void DuplicateOpenRowsForOnePlayer_AreCollapsedToOne()
    {
        var plan = InjuryReconciler.Plan([Open(1, 8471675), Open(2, 8471675)], [Listed(8471675)]);
        Assert.Equal(2, Assert.Single(plan.ToResolve));
        Assert.Empty(plan.ToOpen);
    }

    [Fact]
    public void TheThreeOutcomes_AreDecidedIndependentlyPerPlayer()
    {
        var plan = InjuryReconciler.Plan(
            [Open(1, 100, "Knee"), Open(2, 200, "Hip"), Open(3, 300, "Wrist")],
            [Listed(200, "Lower Body"), Listed(300, "Wrist"), Listed(400, "Shoulder")]);

        Assert.Equal(400, Assert.Single(plan.ToOpen).PlayerId);   // newly listed
        Assert.Equal((2, "Lower Body"), Assert.Single(plan.ToRelabel)); // reworded
        Assert.Equal(1, Assert.Single(plan.ToResolve));           // gone from the page
    }
}
