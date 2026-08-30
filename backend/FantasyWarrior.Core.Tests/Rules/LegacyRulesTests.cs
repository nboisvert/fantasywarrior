using FantasyWarrior.Core.Drafts;
using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Rules;

/// <summary>
/// The conversion the data migration runs once against production. Tested here
/// because it is pure, and because it only gets one chance to be right.
/// </summary>
public class LegacyRulesTests
{
    /// <summary>
    /// Les Mordus exactly as the database holds them today: the cap, bounds,
    /// slots and draft rounds `seed-mordus` wrote, and the three off-season
    /// columns still NULL because nothing has ever had a writer for two of them.
    /// </summary>
    private static LegacyLeagueRules MordusToday() => new(
        CapAmount: 134_000_000,
        DefaultCapHit: 1_000_000,
        RosterMin: 23,
        RosterMax: 35,
        ActiveForwards: 9,
        ActiveDefense: 4,
        ActiveGoalies: 1,
        DraftRounds: 3,
        ProtectionSlots: null,
        StealRounds: null,
        MaxLossesPerTeam: null,
        HasFranchiseSlots: true,
        Scale: new Dictionary<string, double>
        {
            [StatKeys.Goals] = 1,
            [StatKeys.Assists] = 1,
            [StatKeys.Wins] = 2,
            [StatKeys.OtLosses] = 1,
            [StatKeys.Shutouts] = 0,
            [StatKeys.TeamWins] = 2,
            [StatKeys.TeamOtLosses] = 1,
            [StatKeys.TeamLosses] = 0,
        });

    [Fact]
    public void EveryColumnLandsWhereItBelongs()
    {
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Equal(134_000_000, rules.Cap.Max);
        Assert.Equal(1_000_000, rules.Cap.DefaultCapHit);
        Assert.Equal(23, rules.Roster.Min);
        Assert.Equal(35, rules.Roster.Max);
        Assert.Equal(9, rules.Lineup.Slots.Forwards);
        Assert.Equal(4, rules.Lineup.Slots.Defense);
        Assert.Equal(1, rules.Lineup.Slots.Goalies);
        Assert.Equal(3, rules.Draft.RookieRounds);
        Assert.True(rules.Roster.FranchiseSlot);
    }

    [Fact]
    public void TheScaleIsCopiedWhole_IncludingTheFranchiseKeys()
    {
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Equal(MordusToday().Scale, rules.Scoring.Values);
        Assert.Equal(2, rules.Scoring.Values[StatKeys.TeamWins]);
    }

    [Fact]
    public void TheScaleIsCopied_NotAliased()
    {
        // The migration converts one league after another off rows it re-reads;
        // handing back the caller's dictionary would let a later edit reach into
        // a document already written.
        var legacy = MordusToday();
        var rules = LegacyRules.ToRuleSet(legacy);

        rules.Scoring.Values[StatKeys.Hits] = 1;

        Assert.DoesNotContain(StatKeys.Hits, legacy.Scale.Keys);
    }

    [Fact]
    public void ANullStealRoundCountBecomesZero_WhichIsWhatItAlreadyMeant()
    {
        // It reached `?? 0` at the point of use and yielded a draft with no
        // steal segment. Zero says that outright instead of hiding it in a
        // fall-through nobody could see from the Leagues row.
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Equal(0, rules.Draft.Steal.Rounds);
        Assert.Equal(UnprotectedDisposition.StealRounds, rules.Draft.UnprotectedDisposition);
    }

    [Fact]
    public void ANullProtectionSlotCountStaysNull_BecauseNullIsNotZero()
    {
        // "The league has no protection rule" and "protect nobody" are different
        // answers, and the autofill refuses on exactly that distinction.
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Null(rules.Protection.Slots);
        Assert.Null(rules.Draft.Steal.MaxLossesPerTeam);
    }

    [Fact]
    public void TheHardcodedAutoProtectionBarsAreWrittenDown()
    {
        // They were two consts nobody could see from the league's settings.
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.True(rules.Protection.Auto.Enabled);
        Assert.Equal(ProtectionRules.MaxCareerGamesSkater, rules.Protection.Auto.SkaterMaxCareerGames);
        Assert.Equal(ProtectionRules.MaxCareerGamesGoalie, rules.Protection.Auto.GoalieMaxCareerGames);
    }

    [Fact]
    public void EveryRuleTheOldShapeCouldNotExpressTakesTheBehaviourTheCodeHad()
    {
        // A league with no way to say "playoffs do not score" was playing that
        // rule anyway, because the filter was in the rollup job. Writing it down
        // is what makes the document true on the day it is created.
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Equal(PoolType.Keeper, rules.PoolType);
        Assert.Null(rules.Cap.Min);
        Assert.True(rules.Roster.ByPosition.IsEmpty);
        Assert.Equal(LineupMode.ActiveSelection, rules.Lineup.Mode);
        Assert.Equal(MissingLineupBehaviour.CarryForward, rules.Lineup.OnMissing);
        Assert.False(rules.Scoring.IncludePlayoffs);
        Assert.Empty(rules.Scoring.ByPosition);
        Assert.True(rules.Trades.Enabled);
        Assert.True(rules.Trades.PicksTradable);
        Assert.Equal(1, rules.Trades.PickYearsAhead);
        Assert.Equal(TradeApproval.None, rules.Trades.Approval);
        Assert.Null(rules.Protection.SlotsByPosition);
        Assert.Equal(AfterDraftDisposition.StayWithTeam, rules.Protection.AfterDraft);
        Assert.False(rules.Draft.Steal.TurnsTradable);
        Assert.False(rules.Draft.Snake);
        Assert.Equal(FreeAgencyMode.None, rules.FreeAgency.Mode);
    }

    [Fact]
    public void AConvertedLeagueIsValidAndFullySupported()
    {
        // The migration must not produce a document the app would then refuse to
        // save, nor one it would badge as inert — every league converted is
        // playing exactly what the code already did.
        var rules = LegacyRules.ToRuleSet(MordusToday());

        Assert.Empty(RuleSetValidation.Validate(rules));
        Assert.Empty(RuleSetCapabilities.Unsupported(rules));
    }

    [Fact]
    public void ALeagueWithNoFranchiseSlotsIsConvertedWithoutOne()
    {
        // Every league created through POST /api/leagues, which opens no T
        // spots. Its scale carries no team* keys either, so validation holds.
        var plain = MordusToday() with
        {
            HasFranchiseSlots = false,
            Scale = RuleSetDefaults.StartingScale(),
        };

        var rules = LegacyRules.ToRuleSet(plain);

        Assert.False(rules.Roster.FranchiseSlot);
        Assert.Empty(RuleSetValidation.Validate(rules));
    }

    [Fact]
    public void ALeagueThatNeverConfiguredAnythingConvertsCleanly()
    {
        // The shape POST /api/leagues writes: zero slots, no bounds, no draft.
        var blank = new LegacyLeagueRules(
            CapAmount: null, DefaultCapHit: 1_000_000,
            RosterMin: null, RosterMax: null,
            ActiveForwards: 0, ActiveDefense: 0, ActiveGoalies: 0,
            DraftRounds: null, ProtectionSlots: null, StealRounds: null, MaxLossesPerTeam: null,
            HasFranchiseSlots: false,
            Scale: RuleSetDefaults.StartingScale());

        var rules = LegacyRules.ToRuleSet(blank);

        Assert.Empty(RuleSetValidation.Validate(rules));
        Assert.Empty(RuleSetCapabilities.Unsupported(rules));
        Assert.Equal(0, rules.Lineup.Slots.Total);
    }
}
