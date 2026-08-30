using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;

namespace FantasyWarrior.Core.Tests.Drafts;

public class ProtectionAutofillTests
{
    private static readonly AutoProtectConfig Auto = new();

    private static int _nextSpotId = 1;

    /// <summary>A veteran: enough games that only a slot can save him.</summary>
    private static ProtectionCandidate Vet(int teamId, double points, long playerId = 0, string pos = "F") =>
        new(_nextSpotId++, teamId, playerId == 0 ? _nextSpotId : playerId, pos, 400, points);

    // --- the ranking itself ---

    [Fact]
    public void Protects_TheTopScorers_PerTeam()
    {
        var best = Vet(1, 90);
        var second = Vet(1, 80);
        var third = Vet(1, 70);

        var chosen = ProtectionAutofill.Choose([third, best, second], slots: 2, Auto);

        Assert.Equal([best.RosterSpotId, second.RosterSpotId], chosen);
    }

    /// <summary>
    /// Each GM spends his own slots. A high-scoring roster must not eat the
    /// protections of a weak one.
    /// </summary>
    [Fact]
    public void Teams_DoNotSpendEachOthersSlots()
    {
        var richA = Vet(1, 100);
        var richB = Vet(1, 99);
        var poor = Vet(2, 5);

        var chosen = ProtectionAutofill.Choose([richA, richB, poor], slots: 1, Auto);

        Assert.Equal(2, chosen.Count);
        Assert.Contains(richA.RosterSpotId, chosen);
        Assert.Contains(poor.RosterSpotId, chosen);
    }

    [Fact]
    public void FewerEligibleThanSlots_ProtectsWhatThereIs()
    {
        var only = Vet(1, 10);

        Assert.Equal([only.RosterSpotId], ProtectionAutofill.Choose([only], slots: 9, Auto));
    }

    [Fact]
    public void ZeroSlots_ProtectsNobody()
    {
        Assert.Empty(ProtectionAutofill.Choose([Vet(1, 100), Vet(1, 90)], slots: 0, Auto));
    }

    /// <summary>
    /// The endpoint clears and rewrites the whole league on every run, so a
    /// tie broken arbitrarily would reshuffle who is exposed each time the
    /// commissioner pressed the button.
    /// </summary>
    [Fact]
    public void Ties_BreakOnPlayerId_SoRunsAreReproducible()
    {
        var later = new ProtectionCandidate(10, 1, PlayerId: 900, "F", 400, 50);
        var earlier = new ProtectionCandidate(11, 1, PlayerId: 100, "F", 400, 50);

        Assert.Equal([earlier.RosterSpotId], ProtectionAutofill.Choose([later, earlier], slots: 1, Auto));
        Assert.Equal([earlier.RosterSpotId], ProtectionAutofill.Choose([earlier, later], slots: 1, Auto));
    }

    // --- who does not need a slot, and therefore does not get one ---

    /// <summary>
    /// The load-bearing case. An auto-protected prospect is out of reach for
    /// free, so a slot spent on him is a slot thrown away — and the veteran it
    /// would have covered gets exposed instead.
    /// </summary>
    [Fact]
    public void AutoProtected_IsNeitherChosen_NorSpendsASlot()
    {
        var prospect = new ProtectionCandidate(1, 1, 500, "F", CareerNhlGames: 12, Points: 999);
        var vet = new ProtectionCandidate(2, 1, 501, "F", CareerNhlGames: 400, Points: 40);

        var chosen = ProtectionAutofill.Choose([prospect, vet], slots: 1, Auto);

        Assert.Equal([vet.RosterSpotId], chosen);
    }

    /// <summary>
    /// A goalie is measured at 50, not 100 — so one at 75 games is exposed and
    /// does need a slot, where a skater at 75 would not.
    /// </summary>
    [Fact]
    public void GoalieThreshold_IsTheGoalieOne()
    {
        var goalie = new ProtectionCandidate(1, 1, 500, "G", CareerNhlGames: 75, Points: 30);
        var skater = new ProtectionCandidate(2, 1, 501, "F", CareerNhlGames: 75, Points: 99);

        Assert.Equal([goalie.RosterSpotId], ProtectionAutofill.Choose([goalie, skater], slots: 5, Auto));
    }

    /// <summary>
    /// Unknown games is not zero games. DraftPool already refuses to draft such
    /// a player, so he is safe without a slot — burning one on him would waste it
    /// exactly as an auto-protected prospect would.
    /// </summary>
    [Fact]
    public void UnknownCareerGames_IsNeitherChosen_NorSpendsASlot()
    {
        var unsynced = new ProtectionCandidate(1, 1, 500, "F", CareerNhlGames: null, Points: 999);
        var vet = new ProtectionCandidate(2, 1, 501, "F", CareerNhlGames: 400, Points: 40);

        Assert.Equal([vet.RosterSpotId], ProtectionAutofill.Choose([unsynced, vet], slots: 1, Auto));
    }

    /// <summary>
    /// The Équipe slot holds a franchise, which a draft has no way to take.
    /// Its "points" are real — the franchise banks its own record — so without
    /// this rule a good NHL club would outrank every player on the roster.
    /// </summary>
    [Fact]
    public void FranchiseSlot_IsNeverChosen()
    {
        var franchise = new ProtectionCandidate(1, 1, 0, "T", CareerNhlGames: null, Points: 999);
        var vet = new ProtectionCandidate(2, 1, 501, "F", CareerNhlGames: 400, Points: 40);

        Assert.Equal([vet.RosterSpotId], ProtectionAutofill.Choose([franchise, vet], slots: 9, Auto));
    }
}
