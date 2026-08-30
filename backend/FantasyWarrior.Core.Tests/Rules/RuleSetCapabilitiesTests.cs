using FantasyWarrior.Core.Rules;
using FantasyWarrior.Core.Scoring;

namespace FantasyWarrior.Core.Tests.Rules;

public class RuleSetCapabilitiesTests
{
    private static IReadOnlyList<string> Paths(RuleSet rules) =>
        RuleSetCapabilities.Unsupported(rules).Select(g => g.Path).ToList();

    [Fact]
    public void ANewLeaguesDefaultsAreFullySupported()
    {
        // A league nobody has configured must never be badged: it plays exactly
        // what the code does.
        Assert.Empty(RuleSetCapabilities.Unsupported(RuleSetDefaults.ForNewLeague()));
    }

    [Fact]
    public void LesMordusRulesAreFullySupported()
    {
        // The pool the app is built for. A gap here is a feature that has to
        // ship before October, not a badge in a panel.
        var gaps = RuleSetCapabilities.Unsupported(MordusRuleSet.Build());

        Assert.Empty(gaps);
        Assert.True(RuleSetCapabilities.IsFullySupported(MordusRuleSet.Build()));
    }

    [Theory]
    [MemberData(nameof(UnsupportedValues))]
    public void EachUnsupportedValueIsReportedAtItsOwnPath(string path, Action<RuleSet> set)
    {
        var rules = RuleSetDefaults.ForNewLeague();
        set(rules);

        Assert.Contains(path, Paths(rules));
    }

    public static TheoryData<string, Action<RuleSet>> UnsupportedValues()
    {
        var data = new TheoryData<string, Action<RuleSet>>();
        foreach (var (path, set) in Switches) data.Add(path, set);
        return data;
    }

    /// <summary>
    /// Every value the app does not honour, and the path it is reported at. One
    /// list, read by both the per-value test and the uniqueness one, so a new
    /// gap cannot be added to a single half.
    /// </summary>
    private static readonly (string Path, Action<RuleSet> Set)[] Switches =
    [
        ("poolType", r => r.PoolType = PoolType.SingleSeason),
        ("cap.min", r => r.Cap.Min = 60_000_000),
        ("roster.byPosition", r => r.Roster.ByPosition.Goalies.Max = 3),
        ("lineup.mode", r => r.Lineup.Mode = LineupMode.TopN),
        ("lineup.onMissing", r => r.Lineup.OnMissing = MissingLineupBehaviour.ScoreZero),
        ("scoring.byPosition",
            r => r.Scoring.ByPosition["D"] = new Dictionary<string, double> { [StatKeys.Goals] = 2 }),
        ("scoring.includePlayoffs", r => r.Scoring.IncludePlayoffs = true),
        ("trades.approval", r => r.Trades.Approval = TradeApproval.Commissioner),
        ("trades.pickYearsAhead", r => r.Trades.PickYearsAhead = 3),
        ("protection.slotsByPosition", r => r.Protection.SlotsByPosition = new PositionCounts()),
        ("protection.afterDraft",
            r => r.Protection.AfterDraft = AfterDraftDisposition.ReleasedToFreeAgents),
        ("draft.unprotectedDisposition",
            r => r.Draft.UnprotectedDisposition = UnprotectedDisposition.OpenPool),
        ("draft.steal.turnsTradable", r => r.Draft.Steal.TurnsTradable = true),
        ("draft.snake", r => r.Draft.Snake = true),
        ("freeAgency.mode", r => r.FreeAgency.Mode = FreeAgencyMode.Anytime),
    ];

    [Fact]
    public void TheSupportedSideOfEachSwitchIsNotReported()
    {
        // The whole point of answering by value: cap.max is enforced and
        // cap.min is not, so setting only the ceiling badges nothing.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Cap.Max = 134_000_000;
        rules.Scoring.IncludePlayoffs = false;
        rules.Lineup.Mode = LineupMode.ActiveSelection;
        rules.Draft.Steal.TurnsTradable = false;

        Assert.Empty(RuleSetCapabilities.Unsupported(rules));
    }

    [Fact]
    public void GapAt_FindsTheOneRuleAConsumerIsAboutToActOn()
    {
        var rules = RuleSetDefaults.ForNewLeague();
        rules.Draft.Steal.TurnsTradable = true;

        Assert.NotNull(RuleSetCapabilities.GapAt(rules, "draft.steal.turnsTradable"));
        Assert.Null(RuleSetCapabilities.GapAt(rules, "draft.snake"));
    }

    [Fact]
    public void EveryGapCarriesAMessageSayingWhatActuallyHappens()
    {
        // A badge with no explanation is worse than none: it says a rule is
        // inert without saying what the pool is playing instead.
        var rules = RuleSetDefaults.ForNewLeague();
        rules.PoolType = PoolType.SingleSeason;
        rules.FreeAgency.Mode = FreeAgencyMode.Anytime;
        rules.Draft.Snake = true;

        Assert.All(RuleSetCapabilities.Unsupported(rules), gap =>
        {
            Assert.False(string.IsNullOrWhiteSpace(gap.Path));
            Assert.False(string.IsNullOrWhiteSpace(gap.Message));
        });
    }

    [Fact]
    public void EveryReportedPathIsDistinct()
    {
        // The panel keys its badges off the path, so a duplicate would attach
        // one rule's explanation to another's field.
        var rules = RuleSetDefaults.ForNewLeague();
        foreach (var (_, set) in Switches) set(rules);

        var paths = Paths(rules);
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }
}
