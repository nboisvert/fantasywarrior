using FantasyWarrior.Core.Trades;

namespace FantasyWarrior.Core.Tests.Trades;

public class TradeRulesTests
{
    // Les Mordus: $115M cap, 23-35 roster.
    private const long Cap = 115_000_000;
    private const int Min = 23;
    private const int Max = 35;

    private static long?[] Hits(params long[] amounts) => [.. amounts.Select(a => (long?)a)];

    // --- Impact ---

    [Fact]
    public void Impact_SubtractsWhatLeavesAndAddsWhatArrives()
    {
        var impact = TradeRules.Impact(
            "Mine", capBefore: 100_000_000, countBefore: 25,
            outgoing: Hits(9_000_000), incoming: Hits(4_000_000));

        Assert.Equal(95_000_000, impact.CapAfter);
        Assert.Equal(25, impact.CountAfter);
        Assert.Equal(-5_000_000, impact.CapDelta);
    }

    [Fact]
    public void Impact_MovesTheRosterCount_ByTheDifferenceInBodies()
    {
        // A 2-for-1: this side gives two and takes one, so it shrinks.
        var impact = TradeRules.Impact(
            "Mine", capBefore: 100_000_000, countBefore: 25,
            outgoing: Hits(5_000_000, 3_000_000), incoming: Hits(4_000_000));

        Assert.Equal(24, impact.CountAfter);
        Assert.Equal(-1, impact.CountDelta);
    }

    [Fact]
    public void Impact_TreatsAnUnknownContractAsZero_ButCountsIt()
    {
        // Counting it as zero matches vStandings, which is what CapBefore came
        // from — the two have to agree or adding them is meaningless. Counting
        // it separately is what stops the total from lying silently.
        var impact = TradeRules.Impact(
            "Mine", capBefore: 100_000_000, countBefore: 25,
            outgoing: [], incoming: [null, 4_000_000]);

        Assert.Equal(104_000_000, impact.CapAfter);
        Assert.Equal(27, impact.CountAfter);
        Assert.Equal(1, impact.UnknownContracts);
    }

    [Fact]
    public void Impact_CountsUnknownsOnBothSides()
    {
        var impact = TradeRules.Impact(
            "Mine", capBefore: 0, countBefore: 25, outgoing: [null], incoming: [null, null]);

        Assert.Equal(3, impact.UnknownContracts);
    }

    [Fact]
    public void Impact_ChangesNothing_ForAPicksOnlyTrade()
    {
        // Picks are simply absent from both collections: no salary, no spot.
        var impact = TradeRules.Impact(
            "Mine", capBefore: 100_000_000, countBefore: 25, outgoing: [], incoming: []);

        Assert.Equal(100_000_000, impact.CapAfter);
        Assert.Equal(25, impact.CountAfter);
        Assert.Empty(TradeRules.Validate(impact, Cap, Min, Max));
    }

    // --- Validate: the cap ---

    [Fact]
    public void Validate_AcceptsATradeThatStaysUnderTheCap()
    {
        var impact = TradeRules.Impact("Mine", 100_000_000, 25, Hits(9_000_000), Hits(4_000_000));
        Assert.Empty(TradeRules.Validate(impact, Cap, Min, Max));
    }

    [Fact]
    public void Validate_AcceptsLandingExactlyOnTheCap()
    {
        var impact = TradeRules.Impact("Mine", Cap - 1_000_000, 25, [], Hits(1_000_000));

        Assert.Equal(Cap, impact.CapAfter);
        Assert.Empty(TradeRules.Validate(impact, Cap, Min, Max));
    }

    [Fact]
    public void Validate_RejectsOneDollarOverTheCap()
    {
        var impact = TradeRules.Impact("Mine", Cap, 25, [], Hits(1));
        var errors = TradeRules.Validate(impact, Cap, Min, Max);

        Assert.Single(errors);
        Assert.Contains("over the", errors[0]);
    }

    [Fact]
    public void Validate_NamesTheTeamInEveryMessage()
    {
        // The offending side is very often the *other* one, and "over the cap"
        // without a name is unactionable.
        var impact = TradeRules.Impact("Martin", Cap, 25, [], Hits(2_000_000));
        Assert.All(TradeRules.Validate(impact, Cap, Min, Max), e => Assert.StartsWith("Martin", e));
    }

    // --- Validate: roster bounds ---

    [Fact]
    public void Validate_RejectsGoingOverTheRosterMaximum()
    {
        var impact = TradeRules.Impact("Mine", 0, Max, [], Hits(1_000_000));
        var errors = TradeRules.Validate(impact, capAmount: null, Min, Max);

        Assert.Single(errors);
        Assert.Contains("maximum", errors[0]);
    }

    [Fact]
    public void Validate_RejectsDroppingUnderTheRosterMinimum()
    {
        var impact = TradeRules.Impact("Mine", 0, Min, Hits(1_000_000), []);
        var errors = TradeRules.Validate(impact, capAmount: null, Min, Max);

        Assert.Single(errors);
        Assert.Contains("minimum", errors[0]);
    }

    [Fact]
    public void Validate_AcceptsLandingExactlyOnEitherBound()
    {
        var atMax = TradeRules.Impact("Mine", 0, Max - 1, [], Hits(1_000_000));
        var atMin = TradeRules.Impact("Mine", 0, Min + 1, Hits(1_000_000), []);

        Assert.Empty(TradeRules.Validate(atMax, null, Min, Max));
        Assert.Empty(TradeRules.Validate(atMin, null, Min, Max));
    }

    // --- the two-sided case, which is the whole reason this module exists ---

    [Fact]
    public void Validate_CatchesA2For1_BreakingTheMaxOnOneSideAndTheMinOnTheOther()
    {
        // One trade, opposite directions. The team receiving two goes over the
        // maximum; the team sending two falls under the minimum. Neither side
        // can be checked alone.
        var receiving = TradeRules.Impact(
            "Martin", 0, countBefore: Max, outgoing: Hits(1_000_000), incoming: Hits(2_000_000, 3_000_000));
        var sending = TradeRules.Impact(
            "Mine", 0, countBefore: Min, outgoing: Hits(2_000_000, 3_000_000), incoming: Hits(1_000_000));

        var errors = TradeRules.Validate(receiving, null, Min, Max)
            .Concat(TradeRules.Validate(sending, null, Min, Max))
            .ToList();

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.StartsWith("Martin") && e.Contains("maximum"));
        Assert.Contains(errors, e => e.StartsWith("Mine") && e.Contains("minimum"));
    }

    [Fact]
    public void Validate_CatchesTheOtherTeamBusting_WhenMineIsFine()
    {
        // The case a naive one-sided check would wave through.
        var mine = TradeRules.Impact("Mine", 90_000_000, 25, Hits(10_000_000), Hits(1_000_000));
        var theirs = TradeRules.Impact("Martin", Cap - 2_000_000, 25, Hits(1_000_000), Hits(10_000_000));

        Assert.Empty(TradeRules.Validate(mine, Cap, Min, Max));
        Assert.Single(TradeRules.Validate(theirs, Cap, Min, Max));
    }

    // --- absent rules ---

    [Fact]
    public void Validate_AppliesNoCapRule_WhenTheLeagueHasNoCap()
    {
        // Null is "no such rule", not "a limit of zero".
        var impact = TradeRules.Impact("Mine", 0, 25, [], Hits(500_000_000));
        Assert.Empty(TradeRules.Validate(impact, capAmount: null, rosterMin: null, rosterMax: null));
    }

    [Fact]
    public void Validate_ReturnsEveryViolation_NotOnlyTheFirst()
    {
        // Over the cap and over the roster max at once — the UI shows both.
        var impact = TradeRules.Impact("Mine", Cap, Max, [], Hits(5_000_000));
        Assert.Equal(2, TradeRules.Validate(impact, Cap, Min, Max).Count);
    }
}
