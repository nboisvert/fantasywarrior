using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Rules;

public class RuleSetValidationTests
{
    private static IReadOnlyList<string> Errors(RuleSet rules) => RuleSetValidation.Validate(rules);

    private static void AssertClean(RuleSet rules) =>
        Assert.Empty(RuleSetValidation.Validate(rules));

    [Fact]
    public void ADefaultRuleSetIsValid() => AssertClean(RuleSetDefaults.ForNewLeague());

    [Fact]
    public void LesMordusRulesAreValid() => AssertClean(MordusRuleSet.Build());

    // --- the scale ---

    [Fact]
    public void AnUnknownStatIsRejected()
    {
        // The dangerous case: it would score zero forever, silently, and read as
        // a scoring bug rather than the typo it is.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Scoring.Values["goal"] = 1;

        Assert.Contains(Errors(rules), e => e.Contains("Unknown stat \"goal\""));
    }

    [Fact]
    public void AnUnknownStatInsideAPositionOverrideIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Scoring.ByPosition["D"] = new Dictionary<string, double> { ["goalz"] = 2 };

        Assert.Contains(Errors(rules), e => e.Contains("Unknown stat \"goalz\""));
    }

    [Fact]
    public void AScoringOverrideKeyedByAnythingButAPositionGroupIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Scoring.ByPosition["C"] = new Dictionary<string, double> { [StatKeys.Goals] = 2 };

        Assert.Contains(Errors(rules), e => e.Contains("\"C\" is not a position group"));
    }

    [Fact]
    public void PayingForAFranchiseRecordWithoutAnEquipeSlotIsRejected()
    {
        // Nothing could ever earn it: the keys read a franchise's record, and
        // the league holds no franchise.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.FranchiseSlot = false;
        rules.Scoring.Values[StatKeys.TeamWins] = 2;

        Assert.Contains(Errors(rules), e => e.Contains("no Équipe slot"));
    }

    [Fact]
    public void AZeroValuedFranchiseKeyWithoutAnEquipeSlotIsFine()
    {
        // It pays nothing, so it claims nothing — refusing it would force every
        // league to prune keys it never chose to add.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.FranchiseSlot = false;
        rules.Scoring.Values[StatKeys.TeamWins] = 0;

        AssertClean(rules);
    }

    // --- cap ---

    [Fact]
    public void ACapFloorOverTheCeilingIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Cap.Min = 90_000_000;
        rules.Cap.Max = 80_000_000;

        Assert.Contains(Errors(rules), e => e.Contains("cannot exceed the ceiling"));
    }

    [Fact]
    public void ANegativeDefaultCapHitIsRejected()
    {
        // It would pay a team to hold unsigned players, and every cap total
        // would drift further from the truth the more of them it carried.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Cap.DefaultCapHit = -1;

        Assert.Contains(Errors(rules), e => e.Contains("default cap hit cannot be negative"));
    }

    // --- roster and lineup ---

    [Fact]
    public void ARosterMinimumOverTheMaximumIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.Min = 35;
        rules.Roster.Max = 23;

        Assert.Contains(Errors(rules), e => e.Contains("cannot exceed the maximum"));
    }

    [Fact]
    public void NegativeActiveSlotsAreRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Lineup.Slots.Forwards = -1;

        Assert.Contains(Errors(rules), e => e.Contains("Active forwards"));
    }

    [Fact]
    public void ALineupBiggerThanTheRosterMaximumIsRejected()
    {
        // No team could ever field a full one, and every week would score short
        // with nothing saying why.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.Max = 10;
        rules.Lineup.Slots = new PositionCounts { Forwards = 9, Defense = 4, Goalies = 1 };

        Assert.Contains(Errors(rules), e => e.Contains("no team could ever field a full one"));
    }

    [Fact]
    public void PerPositionMinimumsOverTheRosterMaximumAreRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.Max = 20;
        rules.Roster.ByPosition.Forwards.Min = 15;
        rules.Roster.ByPosition.Defense.Min = 8;

        Assert.Contains(Errors(rules), e => e.Contains("add up to 23, over the roster maximum of 20"));
    }

    [Fact]
    public void PerPositionMaximumsUnderTheRosterMinimumAreRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.Min = 23;
        rules.Roster.ByPosition.Forwards.Max = 10;
        rules.Roster.ByPosition.Defense.Max = 5;
        rules.Roster.ByPosition.Goalies.Max = 2;

        Assert.Contains(Errors(rules), e => e.Contains("under the roster minimum of 23"));
    }

    [Fact]
    public void PartialPerPositionMaximumsDoNotTriggerTheSumCheck()
    {
        // Only two groups are capped, so the third is unbounded and the total
        // cannot be known — reporting a shortfall here would be arithmetic on a
        // number nobody gave.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Roster.Min = 23;
        rules.Roster.ByPosition.Forwards.Max = 10;
        rules.Roster.ByPosition.Defense.Max = 5;

        AssertClean(rules);
    }

    // --- protections and draft ---

    [Fact]
    public void NegativeProtectionSlotsAreRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Protection.Slots = -1;

        Assert.Contains(Errors(rules), e => e.Contains("Protection slots cannot be negative"));
    }

    [Fact]
    public void PerPositionProtectionSlotsOverTheLeagueTotalAreRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Protection.Slots = 9;
        rules.Protection.SlotsByPosition = new PositionCounts { Forwards = 6, Defense = 4, Goalies = 2 };

        Assert.Contains(Errors(rules), e => e.Contains("add up to 12, over the league's 9"));
    }

    [Fact]
    public void AnOpenPoolWithStealRoundsIsRejected()
    {
        // The two say opposite things about the same players.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Draft.UnprotectedDisposition = UnprotectedDisposition.OpenPool;
        rules.Draft.Steal.Rounds = 2;

        Assert.Contains(Errors(rules), e => e.Contains("cannot also be 2 steal round"));
    }

    [Fact]
    public void AnOpenPoolWithNoStealRoundsIsFine()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Draft.UnprotectedDisposition = UnprotectedDisposition.OpenPool;

        AssertClean(rules);
    }

    // --- free agency ---

    [Fact]
    public void WindowedFreeAgencyWithNoWindowsIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.FreeAgency.Mode = FreeAgencyMode.Windows;

        Assert.Contains(Errors(rules), e => e.Contains("none are defined"));
    }

    [Fact]
    public void AWindowEndingBeforeItStartsIsRejected()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.FreeAgency.Mode = FreeAgencyMode.Windows;
        rules.FreeAgency.Windows.Add(new FreeAgencyWindow
        {
            Name = "Summer",
            Start = new DateOnly(2026, 8, 1),
            End = new DateOnly(2026, 7, 1),
        });

        Assert.Contains(Errors(rules), e => e.Contains("ends (2026-07-01) before it starts"));
    }

    [Fact]
    public void OverlappingWindowsAreRejected()
    {
        // "Moves per period" would be counted against two windows at once, with
        // no rule saying which.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.FreeAgency.Mode = FreeAgencyMode.Windows;
        rules.FreeAgency.Windows.Add(new FreeAgencyWindow
        {
            Name = "Summer", Start = new DateOnly(2026, 7, 1), End = new DateOnly(2026, 8, 31),
        });
        rules.FreeAgency.Windows.Add(new FreeAgencyWindow
        {
            Name = "Camp", Start = new DateOnly(2026, 8, 15), End = new DateOnly(2026, 9, 30),
        });

        Assert.Contains(Errors(rules), e => e.Contains("\"Summer\" and \"Camp\" overlap"));
    }

    [Fact]
    public void AdjacentWindowsDoNotOverlap()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.FreeAgency.Mode = FreeAgencyMode.Windows;
        rules.FreeAgency.Windows.Add(new FreeAgencyWindow
        {
            Name = "Summer", Start = new DateOnly(2026, 7, 1), End = new DateOnly(2026, 8, 31),
        });
        rules.FreeAgency.Windows.Add(new FreeAgencyWindow
        {
            Name = "Camp", Start = new DateOnly(2026, 9, 1), End = new DateOnly(2026, 9, 30),
        });

        AssertClean(rules);
    }

    [Fact]
    public void EveryViolationIsReported_NotJustTheFirst()
    {
        // A rules panel shows them all at once — the same convention as
        // LineupRules.Validate and TradeRules.Validate.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Cap.DefaultCapHit = -1;
        rules.Roster.Min = 40;
        rules.Roster.Max = 10;
        rules.Protection.Slots = -3;

        Assert.True(Errors(rules).Count >= 3);
    }
}
