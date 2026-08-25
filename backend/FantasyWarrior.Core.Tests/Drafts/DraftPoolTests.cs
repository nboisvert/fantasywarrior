using FantasyWarrior.Core.Drafts;

namespace FantasyWarrior.Core.Tests.Drafts;

public class DraftPoolTests
{
    private const int Me = 1;
    private const int Rival = 2;

    private static DraftCandidate Rostered(
        string pos = "F", int? career = 400, int owner = Rival,
        bool protectedByGm = false, int losses = 0, long id = 100) =>
        new(id, pos, career, owner, protectedByGm, losses);

    private static DraftCandidate Free(string pos = "F", int? career = 400, long id = 100) =>
        new(id, pos, career, null, false, 0);

    private static string? Steal(DraftCandidate c, int? max = 2) =>
        DraftPool.IneligibleReason(c, DraftSegment.Steal, Me, max);

    private static string? Rookie(DraftCandidate c) =>
        DraftPool.IneligibleReason(c, DraftSegment.Rookie, Me, 2);

    [Fact]
    public void Steal_ARivalsUnprotectedVeteranIsFairGame()
    {
        Assert.Null(Steal(Rostered()));
    }

    [Fact]
    public void Steal_CannotTakeFromYourOwnRoster()
    {
        Assert.Equal("You already hold him.", Steal(Rostered(owner: Me)));
    }

    [Fact]
    public void Steal_CannotTakeAProtectedPlayer()
    {
        Assert.Equal("His GM protected him.", Steal(Rostered(protectedByGm: true)));
    }

    [Fact]
    public void Steal_CannotTakeAFranchise()
    {
        // The Equipe slot only ever moves against another franchise.
        Assert.Equal("A franchise cannot be drafted.", Steal(Rostered(pos: "T", career: null)));
    }

    [Fact]
    public void Steal_CannotTakeAFreeAgent()
    {
        Assert.Contains("rookie rounds", Steal(Free()));
    }

    [Theory]
    [InlineData("G", 50, false)]
    [InlineData("G", 51, true)]
    [InlineData("F", 100, false)]
    [InlineData("F", 101, true)]
    [InlineData("D", 100, false)]
    [InlineData("D", 101, true)]
    public void Steal_AutoProtectionUsesTheGoalieAndSkaterBarsSeparately(
        string pos, int career, bool stealable)
    {
        var reason = Steal(Rostered(pos: pos, career: career));

        if (stealable) Assert.Null(reason);
        else Assert.Contains("auto-protected", reason);
    }

    [Fact]
    public void Steal_UnknownCareerGamesIsNotZero()
    {
        // 11 players in the database have never been synced. Refusing is the
        // only answer that cannot hand someone an untouchable prospect.
        Assert.Equal(
            "His NHL experience is unknown, so he cannot be drafted.",
            Steal(Rostered(career: null)));
    }

    [Fact]
    public void APlayerMovesAtMostOncePerDraft()
    {
        // Found by driving a real draft: without this the pool kept offering a
        // player who had just changed hands, and the unique index refused him
        // only after the GM had tapped his row.
        var moved = Rostered() with { AlreadyTakenThisDraft = true };

        Assert.Equal("He has already been drafted this off-season.", Steal(moved));
        Assert.Equal("He has already been drafted this off-season.", Rookie(moved));
    }

    [Fact]
    public void AlreadyTakenBeatsEveryOtherReason()
    {
        // He is on my own roster now precisely because I took him. "You already
        // hold him" would be true and useless; the honest answer is that he has
        // moved once already.
        var mine = Rostered(owner: Me) with { AlreadyTakenThisDraft = true };

        Assert.Equal("He has already been drafted this off-season.", Steal(mine));
    }

    [Fact]
    public void Steal_ATeamBelowItsLossCapIsStillOpen()
    {
        Assert.Null(Steal(Rostered(losses: 1)));
    }

    [Fact]
    public void Steal_ATeamAtItsLossCapIsClosed()
    {
        Assert.Equal("His team has already lost 2.", Steal(Rostered(losses: 2)));
    }

    [Fact]
    public void Steal_TheQuotaRemovesTheWholeRosterAtOnce()
    {
        // This is the behaviour that makes the pool impossible to cache: one
        // team hitting its cap takes every one of its players out at once.
        var drained = new[]
        {
            Rostered(id: 1, owner: 2, losses: 2),
            Rostered(id: 2, owner: 2, losses: 2),
            Rostered(id: 3, owner: 3, losses: 0),
        };

        var available = DraftPool.Available(drained, DraftSegment.Steal, Me, maxLossesPerTeam: 2);

        Assert.Single(available);
        Assert.Equal(3, available[0].PlayerId);
    }

    [Fact]
    public void Steal_NoLossCapMeansTheQuotaNeverFires()
    {
        // A null limit is "the league has no such rule", not a limit of zero.
        Assert.Null(Steal(Rostered(losses: 9), max: null));
    }

    [Fact]
    public void Rookie_AnUnrosteredPlayerIsAvailable()
    {
        Assert.Null(Rookie(Free()));
    }

    [Fact]
    public void Rookie_ARosteredPlayerIsNotEvenYourOwn()
    {
        Assert.Equal("He is already on a roster.", Rookie(Rostered(owner: Rival)));
        Assert.Equal("He is already on a roster.", Rookie(Rostered(owner: Me)));
    }

    [Fact]
    public void Rookie_AutoProtectionIsIrrelevantToAnUnownedPlayer()
    {
        // Auto-protection governs who may be taken AWAY from a GM. Nobody holds
        // this one, so a prospect with two NHL games is perfectly draftable.
        Assert.Null(Rookie(Free(career: 2)));
    }

    [Fact]
    public void Rookie_UnknownCareerGamesDoesNotBlockAFreeAgent()
    {
        Assert.Null(Rookie(Free(career: null)));
    }

    [Fact]
    public void Rookie_StillRefusesAFranchise()
    {
        Assert.Equal("A franchise cannot be drafted.", Rookie(Free(pos: "T")));
    }

    [Fact]
    public void Available_PreservesInputOrder()
    {
        // The caller has already sorted this the way the screen wants.
        var candidates = new[] { Rostered(id: 9), Rostered(id: 3), Rostered(id: 7) };

        var available = DraftPool.Available(candidates, DraftSegment.Steal, Me, 2);

        Assert.Equal([9L, 3L, 7L], available.Select(c => c.PlayerId));
    }

    [Fact]
    public void Available_CanComeBackEmpty()
    {
        // 14 teams x 2 losses is exactly 28 turns: the pool really can close.
        var allDrained = new[] { Rostered(id: 1, losses: 2), Rostered(id: 2, losses: 2) };

        Assert.Empty(DraftPool.Available(allDrained, DraftSegment.Steal, Me, 2));
    }
}
